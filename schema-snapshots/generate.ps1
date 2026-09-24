#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
$node = (Get-Command node -ErrorAction SilentlyContinue)
if (-not $node) { throw 'Node.js is required.' }
& $node.Source (Join-Path $PSScriptRoot 'generate.mjs') @args
