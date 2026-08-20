Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$stateFile = Join-Path $PSScriptRoot 'build_prompt.state.json'
$saved = $null
if (Test-Path $stateFile) {
    try { $saved = Get-Content $stateFile -Raw | ConvertFrom-Json } catch {}
}
function Get-Saved([string]$name, $default) {
    if ($saved -and ($saved.PSObject.Properties.Name -contains $name)) { return $saved.$name }
    return $default
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'BettrFG Build'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true

$y = 16

function New-Check([string]$text, [bool]$checked) {
    $cb = New-Object System.Windows.Forms.CheckBox
    $cb.Text = $text
    $cb.Checked = $checked
    $cb.Location = New-Object System.Drawing.Point(18, $script:y)
    $cb.Size = New-Object System.Drawing.Size(250, 22)
    $form.Controls.Add($cb)
    $script:y += 28
    return $cb
}

function New-Drop([string]$text, [string[]]$items, [string]$selected) {
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = $text
    $lbl.Location = New-Object System.Drawing.Point(18, ($script:y + 3))
    $lbl.Size = New-Object System.Drawing.Size(60, 20)
    $form.Controls.Add($lbl)
    $dd = New-Object System.Windows.Forms.ComboBox
    $dd.DropDownStyle = 'DropDownList'
    $dd.Location = New-Object System.Drawing.Point(82, $script:y)
    $dd.Size = New-Object System.Drawing.Size(100, 22)
    [void]$dd.Items.AddRange($items)
    $dd.SelectedItem = $selected
    $form.Controls.Add($dd)
    $script:y += 30
    return $dd
}

$cbSteam = New-Check 'Copy plugin to Steam' (Get-Saved 'Steam' $true)
$cbEpic  = New-Check 'Copy plugin to Epic' (Get-Saved 'Epic' $true)
$cbDl    = New-Check 'Copy installer to Downloads' (Get-Saved 'Dl' $true)
$cbKill  = New-Check 'Kill and relaunch Fall Guys' (Get-Saved 'Kill' $true)
$cbEa    = New-Check 'Early access build' (Get-Saved 'Ea' $false)

$state = @{}
$collectors = @()
$hookDir = Join-Path $PSScriptRoot 'build'
if (Test-Path $hookDir) {
    foreach ($hook in (Get-ChildItem -Path $hookDir -Filter 'prompt.*.ps1' | Sort-Object Name)) { . $hook.FullName }
}

$y += 24

$ok = New-Object System.Windows.Forms.Button
$ok.Text = 'Build'
$ok.Location = New-Object System.Drawing.Point(30, $y)
$ok.Size = New-Object System.Drawing.Size(100, 32)
$ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
$form.Controls.Add($ok)
$form.AcceptButton = $ok

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'Compile only'
$cancel.Location = New-Object System.Drawing.Point(150, $y)
$cancel.Size = New-Object System.Drawing.Size(100, 32)
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancel)
$form.CancelButton = $cancel

$form.ClientSize = New-Object System.Drawing.Size(280, ($y + 60))

$result = $form.ShowDialog()

$extra = ''
foreach ($c in $collectors) { $extra += . $c }

$state['Steam'] = $cbSteam.Checked
$state['Epic']  = $cbEpic.Checked
$state['Dl']    = $cbDl.Checked
$state['Kill']  = $cbKill.Checked
$state['Ea']    = $cbEa.Checked
$state | ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8

function B([bool]$v) { if ($v) { '1' } else { '0' } }

$ea = B $cbEa.Checked

if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    $steam = B $cbSteam.Checked
    $epic  = B $cbEpic.Checked
    $dl    = B $cbDl.Checked
    $kill  = B $cbKill.Checked
} else {
    $steam = '0'; $epic = '0'; $dl = '0'; $kill = '0'
}

Write-Output "BFGCHOICE:STEAM=$steam;EPIC=$epic;DL=$dl;KILL=$kill;EA=$ea;$extra"
