# Builds a ~27.6 s test take by joining three VCTK fixtures (f294 + f339 + f294):
# useful with TIMBRATUNE_FAKE_MIC and tools/live-steadiness. All fixtures are 44.1 kHz mono PCM16.
#   ./scripts/make-long-wav.ps1 -Out C:\temp\long.wav
param([Parameter(Mandatory)][string]$Out)
$fx = Join-Path $PSScriptRoot "..\tests\Reyfen.Timbratune.Core.Tests\Fixtures"
$data = New-Object System.Collections.Generic.List[byte]
foreach ($n in 'vctk_f294', 'vctk_f339', 'vctk_f294') {
    $b = [IO.File]::ReadAllBytes((Join-Path $fx "$n.wav"))
    if ([Text.Encoding]::ASCII.GetString($b, 36, 4) -ne 'data') { throw "unexpected header in $n" }
    $data.AddRange([byte[]]$b[44..($b.Length - 1)])
}
$h = New-Object byte[] 44
[Text.Encoding]::ASCII.GetBytes('RIFF').CopyTo($h, 0)
[BitConverter]::GetBytes([int](36 + $data.Count)).CopyTo($h, 4)
[Text.Encoding]::ASCII.GetBytes('WAVEfmt ').CopyTo($h, 8)
[BitConverter]::GetBytes(16).CopyTo($h, 16)
[BitConverter]::GetBytes([int16]1).CopyTo($h, 20)      # PCM
[BitConverter]::GetBytes([int16]1).CopyTo($h, 22)      # mono
[BitConverter]::GetBytes(44100).CopyTo($h, 24)
[BitConverter]::GetBytes(88200).CopyTo($h, 28)
[BitConverter]::GetBytes([int16]2).CopyTo($h, 32)
[BitConverter]::GetBytes([int16]16).CopyTo($h, 34)
[Text.Encoding]::ASCII.GetBytes('data').CopyTo($h, 36)
[BitConverter]::GetBytes([int]$data.Count).CopyTo($h, 40)
[IO.File]::WriteAllBytes($Out, [byte[]]($h + $data.ToArray()))
"$Out : $([Math]::Round($data.Count / 88200, 1)) s"
