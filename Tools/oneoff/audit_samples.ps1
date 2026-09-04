$base = "D:\AIWorkSpace\a2ui-unity-toolkit\Assets\A2UISchemeA\Samples"
$files = @(
    "prompt_media", "prompt_climate", "prompt_rest", "prompt_confirm_hpa",
    "media_card", "cabin_media", "dashboard_home", "poi_complex",
    "coverage_tour", "catalog_all", "list_template",
    "00_full_control_center", "01_figma_button_demo", "02_login_screen_carstore",
    "agent_charge_pick", "agent_confirm_camera", "agent_decide_route",
    "agent_my_pet_board", "agent_pet_preference_grow", "agent_trip_conflict",
    "agent_wash_exception", "degrade_unknown", "invalid_bad_packet"
)
foreach ($f in $files) {
    $p = Join-Path $base ($f + ".v0.8.jsonl")
    if (Test-Path $p) {
        $head = Get-Content $p -TotalCount 2
        $prompt = ($head | Select-String -Pattern "prompt:").Line
        $size = (Get-Item $p).Length
        $trunc = if ($prompt.Length -gt 75) { $prompt.Substring(0, 75) } else { $prompt }
        Write-Host ("{0} ({1}B): {2}" -f $f, $size, $trunc)
    } else {
        Write-Host ("{0}: MISSING" -f $f)
    }
}
