#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Generates a self-signed code signing certificate for local MSIX development/testing.

.DESCRIPTION
    Creates a self-signed certificate matching the Publisher CN in Package.appxmanifest,
    exports it as a .pfx file, and installs it into the local machine's Trusted Root store
    so Windows will accept locally-built MSIX packages without "Unknown Publisher" warnings.

    *** FOR DEVELOPMENT/TESTING ONLY ***
    The final release pipeline MUST use a real OV code signing certificate.
    See the signing.props MSBuild file for how the release pipeline enforces this.

.PARAMETER OutputPath
    Path to write the .pfx file. Defaults to certs/VaporVault-Dev.pfx relative to this script.

.PARAMETER Password
    Password for the .pfx file. Defaults to "VaporVaultDev" for local convenience.
    The release pipeline uses a separate, secret-managed password.
#>
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\certs\VaporVault-Dev.pfx"),
    [string]$Password = "VaporVaultDev"
)

$ErrorActionPreference = "Stop"

# Must match Package.appxmanifest > Identity > Publisher
$subject = "CN=AppPublisher"

Write-Host "Creating self-signed code signing certificate..." -ForegroundColor Cyan
Write-Host "  Subject:    $subject"
Write-Host "  Output:     $OutputPath"

# Create the certificate
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $subject `
    -KeyUsage DigitalSignature `
    -FriendlyName "VaporVault Development Signing Certificate" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
    -NotAfter (Get-Date).AddYears(3)

Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green

# Export to .pfx
$securePwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
$outputDir = Split-Path $OutputPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $securePwd | Out-Null
Write-Host "  Exported PFX to: $OutputPath" -ForegroundColor Green

# Install into Trusted Root so Windows accepts the self-signed MSIX
Write-Host ""
Write-Host "Installing certificate into Local Machine Trusted Root store..." -ForegroundColor Cyan
Write-Host "  (Requires elevation -- this is why the script needs RunAsAdministrator)"

$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
$rootStore.Open("ReadWrite")
$rootStore.Add($cert)
$rootStore.Close()

Write-Host "  Installed successfully." -ForegroundColor Green

# Clean up from Personal store (we only need it in Trusted Root + the PFX file)
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host "Certificate thumbprint: $($cert.Thumbprint)"
Write-Host "PFX file:               $OutputPath"
Write-Host "PFX password:           $Password"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. The build will automatically use this PFX for Debug/dev builds."
Write-Host "  2. For Release builds, set the 'VaporVaultSigningCertFile' MSBuild property"
Write-Host "     to your real OV certificate path. The build will FAIL if it is missing."
Write-Host ""
