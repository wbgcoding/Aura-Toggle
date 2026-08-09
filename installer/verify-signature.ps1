param([Parameter(Mandatory = $true)][string]$Path)

# Get-AuthenticodeSignature's trust check reaches out for the certificate's revocation status
# online (CRL/OCSP) with no timeout of its own. A network that drops those packets instead of
# refusing them - a locked-down corporate firewall, say - makes it hang far longer than any
# installer should sit waiting, potentially forever. Run it in a job with a hard cap instead:
# a check that cannot complete in time is treated the same as a failed one (fail closed), never
# as a pass.
$job = Start-Job -ScriptBlock {
    param($FilePath)
    $signature = Get-AuthenticodeSignature -LiteralPath $FilePath
    $subject = $null
    if ($signature.SignerCertificate) { $subject = $signature.SignerCertificate.Subject }
    [pscustomobject]@{
        Valid   = $signature.Status -eq 'Valid'
        Subject = $subject
    }
} -ArgumentList $Path

if (-not (Wait-Job $job -Timeout 20)) {
    Stop-Job $job
    Remove-Job $job -Force
    exit 1
}

$result = Receive-Job $job
Remove-Job $job -Force

if (-not $result.Valid) {
    exit 1
}

# A valid signature only proves SOME trusted certificate signed this file, not that it is
# Microsoft's - the download is the .NET runtime installer specifically, so the signer has to
# say so too, or a file swapped in from any other validly-signed source would still pass.
# Anchored on the attribute itself: an unanchored match would also accept a subject that merely
# carries the words somewhere inside another field.
if ($result.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(\s*,|$)') {
    exit 1
}

exit 0
