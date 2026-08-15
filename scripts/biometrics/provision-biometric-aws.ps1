# ONEVO Biometric AWS Infrastructure — idempotent provisioning script (Task 1)
# Requires: AWS CLI v2, credentials with IAM admin in ap-south-1
# Usage:
#   $env:AWS_PROFILE = "onevo-staging"
#   $env:ONEVO_BACKEND_ROLE_ARN = "arn:aws:iam::123456789012:role/onevo-backend-compute"
#   .\provision-biometric-aws.ps1 -AccountId 123456789012 -Environment staging

param(
    [Parameter(Mandatory = $true)][string]$AccountId,
    [string]$Environment = "staging",
    [string]$Region = "ap-south-1",
    [string]$BackendComputeRoleArn = $env:ONEVO_BACKEND_ROLE_ARN,
    [string]$CaptureRoleName = "onevo-biometric-capture-client",
    [string]$BackendPolicyName = "onevo-biometric-backend",
    [string]$CapturePolicyName = "onevo-biometric-capture-start-only"
)

$ErrorActionPreference = "Stop"

function Require-AwsCli {
    if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
        throw "AWS CLI not found. Install: winget install Amazon.AWSCLI"
    }
}

Require-AwsCli

if ([string]::IsNullOrWhiteSpace($BackendComputeRoleArn)) {
    throw "Set ONEVO_BACKEND_ROLE_ARN or pass -BackendComputeRoleArn"
}

Write-Host "Creating KMS key in $Region..."
$kms = aws kms create-key --region $Region --description "ONEVO Rekognition Face Liveness ($Environment)" | ConvertFrom-Json
$keyId = $kms.KeyMetadata.KeyId
$keyArn = $kms.KeyMetadata.Arn
Write-Host "KMS KeyId: $keyId"

$captureRoleArn = "arn:aws:iam::${AccountId}:role/${CaptureRoleName}"

$backendPolicy = @{
    Version = "2012-10-17"
    Statement = @(
        @{
            Sid = "RekognitionLivenessControlPlane"
            Effect = "Allow"
            Action = @(
                "rekognition:CreateFaceLivenessSession",
                "rekognition:GetFaceLivenessSessionResults",
                "rekognition:CompareFaces"
            )
            Resource = "*"
        },
        @{
            Sid = "KmsForLivenessSession"
            Effect = "Allow"
            Action = @("kms:GenerateDataKey", "kms:Decrypt")
            Resource = $keyArn
        },
        @{
            Sid = "AssumeCaptureClientRole"
            Effect = "Allow"
            Action = "sts:AssumeRole"
            Resource = $captureRoleArn
        }
    )
} | ConvertTo-Json -Depth 6

$backendPolicyFile = [System.IO.Path]::GetTempFileName()
Set-Content -Path $backendPolicyFile -Value $backendPolicy -Encoding UTF8

Write-Host "Creating backend inline policy on $BackendComputeRoleArn..."
aws iam put-role-policy `
    --role-name ($BackendComputeRoleArn.Split('/')[-1]) `
    --policy-name $BackendPolicyName `
    --policy-document "file://$backendPolicyFile"

$capturePolicy = @{
    Version = "2012-10-17"
    Statement = @(
        @{
            Sid = "StartLivenessSessionOnly"
            Effect = "Allow"
            Action = "rekognition:StartFaceLivenessSession"
            Resource = "*"
            Condition = @{
                StringEquals = @{ "aws:RequestedRegion" = $Region }
            }
        }
    )
} | ConvertTo-Json -Depth 6

$capturePolicyFile = [System.IO.Path]::GetTempFileName()
Set-Content -Path $capturePolicyFile -Value $capturePolicy -Encoding UTF8

$trustPolicy = @{
    Version = "2012-10-17"
    Statement = @(
        @{
            Effect = "Allow"
            Principal = @{ AWS = $BackendComputeRoleArn }
            Action = "sts:AssumeRole"
        }
    )
} | ConvertTo-Json -Depth 6

$trustFile = [System.IO.Path]::GetTempFileName()
Set-Content -Path $trustFile -Value $trustPolicy -Encoding UTF8

Write-Host "Creating capture role $CaptureRoleName..."
try {
    aws iam create-role --role-name $CaptureRoleName --assume-role-policy-document "file://$trustFile" | Out-Null
} catch {
    Write-Host "Role may already exist — continuing"
}

aws iam put-role-policy --role-name $CaptureRoleName --policy-name $CapturePolicyName --policy-document "file://$capturePolicyFile"

$recordPath = Join-Path $PSScriptRoot "2026-08-13-aws-biometric-infra-provisioned.json"
@{
    provisionedAt = (Get-Date).ToUniversalTime().ToString("o")
    environment = $Environment
    region = $Region
    kmsKeyId = $keyId
    kmsKeyArn = $keyArn
    captureRoleArn = $captureRoleArn
    backendComputeRoleArn = $BackendComputeRoleArn
    appsettings = @{
        Biometrics = @{
            Region = $Region
            KmsKeyId = $keyId
            CaptureRoleArn = $captureRoleArn
            LivenessConfidenceThreshold = 90
            FaceMatchThreshold = 90
            AttemptTtlMinutes = 15
        }
    }
} | ConvertTo-Json -Depth 6 | Set-Content -Path $recordPath -Encoding UTF8

Write-Host ""
Write-Host "=== PROVISIONING COMPLETE ==="
Write-Host "Record written: $recordPath"
Write-Host "Set these in staging appsettings / env:"
Write-Host "  Biometrics:Region=$Region"
Write-Host "  Biometrics:KmsKeyId=$keyId"
Write-Host "  Biometrics:CaptureRoleArn=$captureRoleArn"
