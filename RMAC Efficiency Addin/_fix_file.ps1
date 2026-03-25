$filePath = "D:\Coding Projects\RMVS02 RMAC Efficiency\RMAC Efficiency Addin\RMAC Efficiency Addin\StandardAddInServer.cs"
$lines = [System.IO.File]::ReadAllLines($filePath)

$newLines = [System.Collections.Generic.List[string]]::new()
$skipBlanksBefore = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '_MARKER_4_') {
        # Remove trailing blank lines that were added before this
        while ($newLines.Count -gt 0 -and [string]::IsNullOrWhiteSpace($newLines[$newLines.Count - 1])) {
            $newLines.RemoveAt($newLines.Count - 1)
        }
        # Add one blank line
        $newLines.Add("")
        continue
    }
    $newLines.Add($lines[$i])
}

[System.IO.File]::WriteAllLines($filePath, $newLines.ToArray())
Write-Host "Done"
