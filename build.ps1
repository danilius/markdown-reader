# Builds the single-file MarkdownReader.html from src/reader.template.html
# by inlining the vendored marked and DOMPurify libraries.
$root = $PSScriptRoot
$tpl    = [IO.File]::ReadAllText("$root\src\reader.template.html")
$marked = [IO.File]::ReadAllText("$root\marked.min.js")
$purify = [IO.File]::ReadAllText("$root\purify.min.js")
if ($marked.Contains("</script>") -or $purify.Contains("</script>")) { throw "library contains </script>" }
$out = $tpl.Replace("/*__MARKED__*/", $marked).Replace("/*__PURIFY__*/", $purify)
[IO.File]::WriteAllText("$root\MarkdownReader.html", $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Built MarkdownReader.html ($([math]::Round((Get-Item "$root\MarkdownReader.html").Length/1KB)) KB)"
