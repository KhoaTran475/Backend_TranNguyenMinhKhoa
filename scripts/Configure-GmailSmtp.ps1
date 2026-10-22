param(
    [string]$Email = 'khoatran472005@gmail.com'
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\trannguyenminhkhoa_webAPI\trannguyenminhkhoa_webAPI.csproj'
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw 'API project was not found.'
}
try { $parsedEmail = New-Object System.Net.Mail.MailAddress($Email) }
catch { throw 'Enter a valid Gmail address using -Email.' }
if ($parsedEmail.Address -ne $Email) { throw 'Use an email address without a display name.' }

Write-Host ('Configure Gmail SMTP for: ' + $Email)
Write-Host 'Create an App Password at https://myaccount.google.com/apppasswords (2-Step Verification required).'
Write-Host 'Enter the Gmail App Password below, NOT your normal Gmail password.'
$secureAppPassword = Read-Host 'Gmail App Password (hidden input)' -AsSecureString
$passwordPointer = [IntPtr]::Zero
$plainAppPassword = $null
$smtpJson = $null
try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureAppPassword)
    $plainAppPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer) -replace '\s', ''
    if ($plainAppPassword -notmatch '^[a-zA-Z]{16}$') {
        throw 'Expected a 16-letter Gmail App Password. No changes were saved.'
    }
    $smtpSettings = @{
        'Smtp:Host' = 'smtp.gmail.com'
        'Smtp:Port' = '587'
        'Smtp:Security' = 'StartTls'
        'Smtp:Username' = $Email
        'Smtp:Sender' = $Email
        'Smtp:Password' = $plainAppPassword
    }
    # Read-Host input is not placed in shell command history or source files.
    foreach ($setting in $smtpSettings.GetEnumerator()) {
        & dotnet user-secrets set $setting.Key $setting.Value --project $projectPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not save SMTP User Secrets.' }
    }
    Write-Host 'SMTP settings saved. Restart the API, then POST /api/auth/login to receive an OTP.'
    Write-Host 'Saving settings does not verify SMTP delivery; check your inbox after login.'
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    $plainAppPassword = $null
    $smtpJson = $null
    if ($null -ne $smtpSettings) { $smtpSettings.Clear() }
    $secureAppPassword.Dispose()
}
