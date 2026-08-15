# AWS Biometric Infrastructure — Setup Runbook

**Plan:** Verified Employee Check-In — Plan 1 (Task 1)  
**Region:** `ap-south-1`  
**Status:** **Ready to execute — AWS CLI installed; credentials required**

> AWS CLI v2 is installed on this machine. Run `aws configure` or `aws login`, then execute:
>
> ```powershell
> cd HRMS-Backend-v1/scripts/biometrics
> $env:ONEVO_BACKEND_ROLE_ARN = "arn:aws:iam::<account-id>:role/<backend-compute-role>"
> .\provision-biometric-aws.ps1 -AccountId <account-id> -Environment staging
> ```
>
> Output record: `scripts/biometrics/2026-08-13-aws-biometric-infra-provisioned.json`

---

## Quick execute (after AWS CLI is configured)

```bash
# 1. Create KMS key (ap-south-1)
aws kms create-key --region ap-south-1 --description "ONEVO Rekognition Face Liveness sessions"

# 2. Create capture-client role + attach StartFaceLivenessSession-only policy (see JSON below)

# 3. Attach backend compute policy (Rekognition control plane + KMS + sts:AssumeRole)

# 4. Set appsettings / env:
#    Biometrics:Region=ap-south-1
#    Biometrics:KmsKeyId=<key-id>
#    Biometrics:CaptureRoleArn=arn:aws:iam::<account>:role/onevo-biometric-capture-client
```

---

## 1. Backend compute role policy

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "RekognitionLivenessControlPlane",
      "Effect": "Allow",
      "Action": [
        "rekognition:CreateFaceLivenessSession",
        "rekognition:GetFaceLivenessSessionResults",
        "rekognition:CompareFaces"
      ],
      "Resource": "*"
    },
    {
      "Sid": "KmsForLivenessSession",
      "Effect": "Allow",
      "Action": ["kms:GenerateDataKey", "kms:Decrypt"],
      "Resource": "arn:aws:kms:ap-south-1:<account-id>:key/<key-id>"
    },
    {
      "Sid": "AssumeCaptureClientRole",
      "Effect": "Allow",
      "Action": "sts:AssumeRole",
      "Resource": "arn:aws:iam::<account-id>:role/onevo-biometric-capture-client"
    }
  ]
}
```

---

## 2. Capture-client role (`onevo-biometric-capture-client`)

**Permission policy:**

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "StartLivenessSessionOnly",
      "Effect": "Allow",
      "Action": "rekognition:StartFaceLivenessSession",
      "Resource": "*",
      "Condition": {
        "StringEquals": { "aws:RequestedRegion": "ap-south-1" }
      }
    }
  ]
}
```

**Trust policy:** allow backend compute role to `sts:AssumeRole`.

---

## 3. Application config mapping

```json
{
  "Biometrics": {
    "Region": "ap-south-1",
    "KmsKeyId": "<key-id>",
    "CaptureRoleArn": "arn:aws:iam::<account-id>:role/onevo-biometric-capture-client",
    "LivenessConfidenceThreshold": 90,
    "FaceMatchThreshold": 90,
    "AttemptTtlMinutes": 15
  }
}
```

---

## Environment matrix (fill after provisioning)

| Environment | Backend role ARN | Capture role ARN | KMS key ID | Verified |
|-------------|------------------|------------------|------------|----------|
| Staging | | | | ☐ |
| Production | | | | ☐ |

---

## Verification after provisioning

1. Staging backend creates enrollment attempt without `AccessDeniedException`.
2. TrayApp WebView2 completes one liveness session (Task 21).
3. No static AWS keys in repo or committed config.
