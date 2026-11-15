[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$labels = @{}
$hasher = [Security.Cryptography.SHA256]::Create()
try {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'DisplayFX/Interface') -Filter '*.xaml' -Recurse -File) {
        $document = New-Object Xml.XmlDocument
        $document.PreserveWhitespace = $true
        $document.Load($file.FullName)
        foreach ($element in $document.SelectNodes('//*')) {
            foreach ($attribute in @($element.Attributes)) {
                if ($attribute.LocalName -notin @('Text','Content','Header','ToolTip','AutomationProperties.Name','AutomationProperties.HelpText','Title')) { continue }
                $text = $attribute.Value
                if ($text -notmatch '[A-Za-z]' -or $text.StartsWith('{')) { continue }
                $hash = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-','').Substring(0,12)
                $key = 'UiText' + $hash
                $labels[$key] = $text
                $attribute.Value = '{DynamicResource ' + $key + '}'
            }
        }
        $document.Save($file.FullName)
    }
    $resourcePath = Join-Path $sourceRoot 'DisplayFX/Resources/ResourceDictionaries/Resources.xaml'
    $resources = New-Object Xml.XmlDocument
    $resources.PreserveWhitespace = $true
    $resources.Load($resourcePath)
    $existing = @{}
    foreach ($element in $resources.DocumentElement.ChildNodes) {
        if ($element -is [Xml.XmlElement]) { $existing[$element.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')] = $element }
    }
    foreach ($key in ($labels.Keys | Sort-Object)) {
        if ($existing.ContainsKey($key)) {
            if ($existing[$key].InnerText -ne $labels[$key]) { throw "UI string key collision: $key" }
            continue
        }
        $node = $resources.CreateElement('sys','String','clr-namespace:System;assembly=mscorlib')
        $attribute = $resources.CreateAttribute('x','Key','http://schemas.microsoft.com/winfx/2006/xaml')
        $attribute.Value = $key
        [void]$node.Attributes.Append($attribute)
        $node.InnerText = $labels[$key]
        [void]$resources.DocumentElement.AppendChild($resources.CreateWhitespace("`r`n    "))
        [void]$resources.DocumentElement.AppendChild($node)
    }
    $resources.Save($resourcePath)
    Write-Output "Synchronized $($labels.Count) new/literal labels. Add their Dutch translations to LocalizationController.cs."
}
finally { $hasher.Dispose() }
