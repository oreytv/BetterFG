param(
    [switch]$Silent,
    [string]$ConfigPath
)

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $PSScriptRoot 'creator_deploy.txt'
}

function Read-Targets {
    if (-not (Test-Path $ConfigPath)) { return @() }
    $rows = @()
    foreach ($line in (Get-Content -LiteralPath $ConfigPath -Encoding UTF8)) {
        $t = $line.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $on = $true
        if ($t -match '^([01])\|(.+)$') {
            $on = $Matches[1] -eq '1'
            $t = $Matches[2]
        }
        $rows += [pscustomobject]@{ Enabled = $on; Path = $t }
    }
    return $rows
}

function Write-Targets($rows) {
    $out = foreach ($r in $rows) { ('{0}|{1}' -f $(if ($r.Enabled) { '1' } else { '0' }), $r.Path) }
    Set-Content -LiteralPath $ConfigPath -Value $out -Encoding UTF8
}

$targets = @(Read-Targets)

if (-not $Silent) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace BfgDeploy
{
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    internal class FileOpenDialog { }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    public static class FolderPicker
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        public static string Pick(IntPtr owner, string title, string startPath)
        {
            IFileDialog dlg = (IFileDialog)(new FileOpenDialog());
            dlg.SetOptions(0x20 | 0x40 | 0x800 | 0x8);
            dlg.SetTitle(title);

            if (!string.IsNullOrEmpty(startPath) && System.IO.Directory.Exists(startPath))
            {
                Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                IShellItem start;
                SHCreateItemFromParsingName(startPath, IntPtr.Zero, ref shellItemGuid, out start);
                dlg.SetFolder(start);
            }

            if (dlg.Show(owner) != 0) { return null; }

            IShellItem item;
            dlg.GetResult(out item);
            IntPtr buf;
            item.GetDisplayName(0x80058000, out buf);
            string path = Marshal.PtrToStringUni(buf);
            Marshal.FreeCoTaskMem(buf);
            return path;
        }
    }
}
'@

    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'BettrFG Creator Deploy'
    $form.ClientSize = New-Object System.Drawing.Size(520, 320)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'Sizable'
    $form.MinimumSize = New-Object System.Drawing.Size(460, 300)
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true

    $hint = New-Object System.Windows.Forms.Label
    $hint.Text = 'Tick the Unity project Editor folders to copy BetterFG.Creator.dll into.'
    $hint.Location = New-Object System.Drawing.Point(14, 12)
    $hint.Size = New-Object System.Drawing.Size(490, 18)
    $form.Controls.Add($hint)

    $list = New-Object System.Windows.Forms.CheckedListBox
    $list.Location = New-Object System.Drawing.Point(14, 36)
    $list.Size = New-Object System.Drawing.Size(492, 190)
    $list.CheckOnClick = $true
    $list.IntegralHeight = $false
    $list.HorizontalScrollbar = $true
    $list.Anchor = 'Top,Left,Right,Bottom'
    $form.Controls.Add($list)

    foreach ($t in $targets) { [void]$list.Items.Add($t.Path, $t.Enabled) }

    $add = New-Object System.Windows.Forms.Button
    $add.Text = 'Add folder...'
    $add.Location = New-Object System.Drawing.Point(14, 234)
    $add.Size = New-Object System.Drawing.Size(110, 28)
    $add.Anchor = 'Left,Bottom'
    $add.Add_Click({
        $seed = ''
        if ($list.SelectedIndex -ge 0) { $seed = [string]$list.Items[$list.SelectedIndex] }
        elseif ($list.Items.Count -gt 0) { $seed = [string]$list.Items[0] }
        $p = [BfgDeploy.FolderPicker]::Pick($form.Handle, 'Pick a Unity project Assets\Editor folder', $seed)
        if ($p) {
            $dupe = $false
            foreach ($i in $list.Items) { if ($i -eq $p) { $dupe = $true } }
            if (-not $dupe) { [void]$list.Items.Add($p, $true) }
        }
    })
    $form.Controls.Add($add)

    $remove = New-Object System.Windows.Forms.Button
    $remove.Text = 'Remove'
    $remove.Location = New-Object System.Drawing.Point(132, 234)
    $remove.Size = New-Object System.Drawing.Size(90, 28)
    $remove.Anchor = 'Left,Bottom'
    $remove.Add_Click({
        if ($list.SelectedIndex -ge 0) { $list.Items.RemoveAt($list.SelectedIndex) }
    })
    $form.Controls.Add($remove)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'Deploy'
    $ok.Location = New-Object System.Drawing.Point(296, 272)
    $ok.Size = New-Object System.Drawing.Size(100, 32)
    $ok.Anchor = 'Right,Bottom'
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Controls.Add($ok)
    $form.AcceptButton = $ok

    $skip = New-Object System.Windows.Forms.Button
    $skip.Text = 'Compile only'
    $skip.Location = New-Object System.Drawing.Point(406, 272)
    $skip.Size = New-Object System.Drawing.Size(100, 32)
    $skip.Anchor = 'Right,Bottom'
    $skip.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $form.Controls.Add($skip)
    $form.CancelButton = $skip

    $result = $form.ShowDialog()

    $rows = @()
    for ($i = 0; $i -lt $list.Items.Count; $i++) {
        $rows += [pscustomobject]@{ Enabled = $list.GetItemChecked($i); Path = [string]$list.Items[$i] }
    }
    Write-Targets $rows

    if ($result -ne [System.Windows.Forms.DialogResult]::OK) { return }
    $targets = $rows
}

foreach ($t in $targets) {
    if ($t.Enabled -and $t.Path) { Write-Output ("CREATORDEPLOY:" + $t.Path) }
}
