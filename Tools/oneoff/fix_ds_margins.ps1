$path = "D:\AIWorkSpace\a2ui-unity-toolkit\Assets\A2UISchemeA\Styles\DS\A2uiAlias.uss"
$content = Get-Content $path -Raw

# margin: 0 -> margin-top:0; margin-left:0; margin-right:0  (保留 margin-bottom 给 Column 间距)
# 用 4 个空格缩进匹配（这些都在规则块内部）
$content = $content -replace '(\s+)margin: 0;', "`$1margin-top: 0;`$1margin-left: 0;`$1margin-right: 0;`$1margin-bottom: var(--space-3, 12px);"

Set-Content $path $content -NoNewline
Write-Host "replaced margin:0 with directional margins + margin-bottom"
# 验证
$count = (Select-String -Path $path -Pattern 'margin-bottom: var\(--space-3').Count
Write-Host ("margin-bottom added: " + $count + " places")
