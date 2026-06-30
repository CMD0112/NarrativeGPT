# Reorganize docs/ into subdirectories and rewrite links.
$ErrorActionPreference = 'Continue'
$root = if ($PSScriptRoot) { Split-Path $PSScriptRoot -Parent } else { "e:\Documents\Code\chatgpt-wrapper" }
Set-Location $root

$reloc = @{
    'adventure-panel.md' = 'user/adventure-panel.md'
    'user-guide.md' = 'user/user-guide.md'
    'user-projects-and-sync.md' = 'user/user-projects-and-sync.md'
    'troubleshooting.md' = 'user/troubleshooting.md'
    'instruction-contract-guide.md' = 'user/instruction-contract-guide.md'
    'instruction-sources-paradigm.md' = 'user/instruction-sources-paradigm.md'
    'instruction-channels.md' = 'user/instruction-channels.md'
    'narrator-settings.md' = 'user/narrator-settings.md'
    'prompt-construction-guide.md' = 'user/prompt-construction-guide.md'
    'entity-canon-change-paradigm.md' = 'user/entity-canon-change-paradigm.md'
    'architecture.md' = 'developer/architecture.md'
    'adventure-developer-reference.md' = 'developer/adventure-developer-reference.md'
    'build-and-deploy.md' = 'developer/build-and-deploy.md'
    'testing.md' = 'developer/testing.md'
    'webview-bridges.md' = 'developer/webview-bridges.md'
    'chatgpt-api-integration.md' = 'developer/chatgpt-api-integration.md'
    'injected-assets.md' = 'developer/injected-assets.md'
    'utility-job-orchestration.md' = 'developer/utility-job-orchestration.md'
    'data-model-reference.md' = 'reference/data-model-reference.md'
    'data-model-audit-cmd86.md' = 'reference/data-model-audit-cmd86.md'
    'services-reference.md' = 'reference/services-reference.md'
    'ui-components.md' = 'reference/ui-components.md'
    'adventure-thread-registry.md' = 'reference/adventure-thread-registry.md'
    'canon-schema.md' = 'reference/canon-schema.md'
    'appearance-theme-settings.md' = 'settings/appearance-theme-settings.md'
    'settings-interactables-inventory.md' = 'settings/settings-interactables-inventory.md'
    'settings-interactables-audit.md' = 'settings/settings-interactables-audit.md'
    'settings-ux-taxonomy.md' = 'settings/settings-ux-taxonomy.md'
    'play-design-surface-convergence-adr.md' = 'adr/play-design-surface-convergence-adr.md'
    'play-send-orchestration-adr.md' = 'adr/play-send-orchestration-adr.md'
    'injection-policy-adr.md' = 'adr/injection-policy-adr.md'
    'narrator-revision-adr.md' = 'adr/narrator-revision-adr.md'
    'utility-job-context-assembly-adr.md' = 'adr/utility-job-context-assembly-adr.md'
    'play-thread-utility-orchestration-adr.md' = 'adr/play-thread-utility-orchestration-adr.md'
    'local-semantic-retrieval-adr.md' = 'adr/local-semantic-retrieval-adr.md'
    'user-message-edit-adr.md' = 'adr/user-message-edit-adr.md'
    'utility-worker-lane-adr.md' = 'adr/utility-worker-lane-adr.md'
    'utility-delivery-pivot-adr.md' = 'adr/utility-delivery-pivot-adr.md'
    'narrative-flight-recorder-adr.md' = 'adr/narrative-flight-recorder-adr.md'
    'play-thread-canonical-adr.md' = 'adr/play-thread-canonical-adr.md'
    'play-send-orchestration-implementation-plan.md' = 'plans/play-send-orchestration-implementation-plan.md'
    'play-thread-utility-orchestration-plan.md' = 'plans/play-thread-utility-orchestration-plan.md'
    'injection-policy-implementation-plan.md' = 'plans/injection-policy-implementation-plan.md'
    'utility-worker-lane-plan.md' = 'plans/utility-worker-lane-plan.md'
    'play-message-edit-refinement-plan.md' = 'plans/play-message-edit-refinement-plan.md'
    'runtime-canon-schema-plan.md' = 'plans/runtime-canon-schema-plan.md'
    'linear-issue-reference.md' = 'linear/linear-issue-reference.md'
    'linear-integration.md' = 'linear/linear-integration.md'
    'chat-file-io-feasibility.md' = 'Enhancements/chat-file-io-feasibility.md'
}

foreach ($d in @('user','developer','reference','settings','adr','linear')) {
    New-Item -ItemType Directory -Force -Path "docs/$d" | Out-Null
}

foreach ($entry in $reloc.GetEnumerator()) {
    $src = "docs/$($entry.Key)"
    $dst = "docs/$($entry.Value)"
    if (-not (Test-Path $src)) { continue }
    $dstDir = Split-Path $dst -Parent
    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
    git mv $src $dst 2>$null | Out-Null
    if (-not (Test-Path $dst)) { Move-Item -Force $src $dst }
}

function Resolve-DocTarget([string]$pathPart) {
    if ([string]::IsNullOrWhiteSpace($pathPart)) { return $null }
    if ($pathPart -match '^(https?|linear|mailto):') { return $null }
    if ($pathPart -match '^\.\./') { return $null }
    if ($pathPart -match '^ChatGPTWrapper/') { return $null }
    if ($pathPart -match '^\.github/') { return $null }
    if ($pathPart -match '/') {
        if ($pathPart -match '^(Enhancements|plans|user|developer|reference|settings|adr|linear)/') {
            return "docs/$pathPart"
        }
        return $null
    }
    $base = $pathPart
    if ($reloc.ContainsKey($base)) { return "docs/$($reloc[$base])" }
    if (Test-Path "docs/Enhancements/$base") { return "docs/Enhancements/$base" }
    if (Test-Path "docs/plans/$base") { return "docs/plans/$base" }
    if ($base -eq 'INDEX.md') { return 'docs/INDEX.md' }
    return $null
}

# Phase 1: repo-wide docs/basename.md
$skip = @('docs-backup-', '\.git\', '\node_modules\', '\bin\', '\obj\')
$allTextFiles = Get-ChildItem -Path $root -Recurse -File |
    Where-Object { $_.Extension -match '^\.(md|mdc|cs|xaml|json|py|yml|yaml|ps1)$' } |
    Where-Object { $p = $_.FullName; -not ($skip | Where-Object { $p -match $_ }) }

foreach ($file in $allTextFiles) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $orig = $content
    foreach ($entry in $reloc.GetEnumerator()) {
        $content = $content.Replace("docs/$($entry.Key)", "docs/$($entry.Value)")
    }
    if ($content -ne $orig) { [IO.File]::WriteAllText($file.FullName, $content) }
}

# Phase 2: relative links in docs/**/*.md
$linkRx = [regex]'\]\(([^)\s]+)\)'
foreach ($file in (Get-ChildItem -Path "docs" -Recurse -Filter "*.md")) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $sb = New-Object System.Text.StringBuilder
    $lastIdx = 0
    foreach ($m in $linkRx.Matches($content)) {
        [void]$sb.Append($content.Substring($lastIdx, $m.Index - $lastIdx))
        $full = $m.Groups[1].Value
        $hashIdx = $full.IndexOf('#')
        $pathPart = if ($hashIdx -ge 0) { $full.Substring(0, $hashIdx) } else { $full }
        $anchor = if ($hashIdx -ge 0) { $full.Substring($hashIdx) } else { '' }
        $targetDoc = Resolve-DocTarget $pathPart
        if ($targetDoc) {
            $fromDir = Split-Path $file.FullName -Parent
            $targetFull = Join-Path $root $targetDoc
            $rel = [System.IO.Path]::GetRelativePath($fromDir, $targetFull) -replace '\\', '/'
            [void]$sb.Append("](" + $rel + $anchor + ")")
        } else {
            [void]$sb.Append($m.Value)
        }
        $lastIdx = $m.Index + $m.Length
    }
    [void]$sb.Append($content.Substring($lastIdx))
    $newContent = $sb.ToString()
    if ($newContent -ne $content) { [IO.File]::WriteAllText($file.FullName, $newContent) }
}

Write-Host "Done. Relocated $($reloc.Count) files under docs/."
