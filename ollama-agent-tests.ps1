# ============================================================
# Ollama Local Agent Test Suite
# ============================================================
#
# Example:
#
#   . .\ollama-agent-tests.ps1
#
#   Test-AgentTools "llama3.1:8b"
#   Test-AgentCapabilities "llama3.1:8b"
#   Test-AgentFull "llama3.1:8b"
#
# Models currently worth testing:
#
#   llama3.1:8b
#   hermes3:8b
#   granite3.3:8b
#
# ============================================================


# ============================================================
# PATH SAFETY
# ============================================================

function Resolve-AgentTestPath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "Path must not be empty."
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root)

    $full = [System.IO.Path]::GetFullPath(
        (Join-Path $rootFull $RelativePath)
    )

    $prefix = $rootFull.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar

    if (
        $full -ne $rootFull -and
        -not $full.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase
        )
    ) {
        throw "Path escapes diagnostic directory: $RelativePath"
    }

    return $full
}


# ============================================================
# TOOL DEFINITIONS GIVEN TO THE MODEL
# ============================================================

function Get-AgentTestTools {

    return @(

        # ----------------------------------------------------
        # WRITE FILE
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "write_file"
                description = "Write text to a file in the diagnostic sandbox."

                parameters = @{
                    type = "object"

                    properties = @{
                        path = @{
                            type = "string"
                        }

                        content = @{
                            type = "string"
                        }
                    }

                    required = @(
                        "path",
                        "content"
                    )

                    additionalProperties = $false
                }
            }
        }


        # ----------------------------------------------------
        # READ FILE
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "read_file"
                description = "Read a text file from the diagnostic sandbox."

                parameters = @{
                    type = "object"

                    properties = @{
                        path = @{
                            type = "string"
                        }
                    }

                    required = @(
                        "path"
                    )

                    additionalProperties = $false
                }
            }
        }


        # ----------------------------------------------------
        # LIST FILES
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "list_files"
                description = "List all files in the diagnostic sandbox."

                parameters = @{
                    type = "object"
                    properties = @{}
                    additionalProperties = $false
                }
            }
        }


        # ----------------------------------------------------
        # REPLACE TEXT
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "replace_text"
                description = "Replace exact text inside a diagnostic file."

                parameters = @{
                    type = "object"

                    properties = @{
                        path = @{
                            type = "string"
                        }

                        old_text = @{
                            type = "string"
                        }

                        new_text = @{
                            type = "string"
                        }
                    }

                    required = @(
                        "path",
                        "old_text",
                        "new_text"
                    )

                    additionalProperties = $false
                }
            }
        }


        # ----------------------------------------------------
        # SHELL COMMAND
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "run_command"
                description = "Run one safe diagnostic command in the current repository."

                parameters = @{
                    type = "object"

                    properties = @{
                        command = @{
                            type = "string"
                        }
                    }

                    required = @(
                        "command"
                    )

                    additionalProperties = $false
                }
            }
        }


        # ----------------------------------------------------
        # HTTP FETCH
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "web_fetch"
                description = "Fetch a web page over the real internet."

                parameters = @{
                    type = "object"

                    properties = @{
                        url = @{
                            type = "string"
                        }
                    }

                    required = @(
                        "url"
                    )

                    additionalProperties = $false
                }
            }
        }


        # ----------------------------------------------------
        # WEB SEARCH
        # ----------------------------------------------------

        @{
            type = "function"

            function = @{
                name = "web_search"
                description = "Search the real internet using Bing RSS search."

                parameters = @{
                    type = "object"

                    properties = @{
                        query = @{
                            type = "string"
                        }
                    }

                    required = @(
                        "query"
                    )

                    additionalProperties = $false
                }
            }
        }
    )
}


# ============================================================
# ACTUAL TOOL IMPLEMENTATIONS
# ============================================================

function Invoke-AgentTestTool {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        $Arguments,

        [Parameter(Mandatory)]
        [string]$TestRoot,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    switch ($Name) {

        # ----------------------------------------------------
        # WRITE FILE
        # ----------------------------------------------------

        "write_file" {

            if (
                $null -eq $Arguments -or
                [string]::IsNullOrWhiteSpace(
                    [string]$Arguments.path
                )
            ) {
                throw "write_file requires a non-empty path"
            }

            $path = Resolve-AgentTestPath `
                -Root $TestRoot `
                -RelativePath ([string]$Arguments.path)

            $parent = Split-Path $path -Parent

            if ($parent) {
                New-Item `
                    -ItemType Directory `
                    -Force `
                    -Path $parent `
                    -ErrorAction Stop |
                    Out-Null
            }

            Set-Content `
                -LiteralPath $path `
                -Value ([string]$Arguments.content) `
                -NoNewline `
                -ErrorAction Stop

            return "SUCCESS: wrote $($Arguments.path)"
        }


        # ----------------------------------------------------
        # READ FILE
        # ----------------------------------------------------

        "read_file" {

            if (
                $null -eq $Arguments -or
                [string]::IsNullOrWhiteSpace(
                    [string]$Arguments.path
                )
            ) {
                throw "read_file requires a non-empty path"
            }

            $path = Resolve-AgentTestPath `
                -Root $TestRoot `
                -RelativePath ([string]$Arguments.path)

            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "File does not exist: $($Arguments.path)"
            }

            return Get-Content `
                -LiteralPath $path `
                -Raw `
                -ErrorAction Stop
        }


        # ----------------------------------------------------
        # LIST FILES
        # ----------------------------------------------------

        "list_files" {

            if (-not (Test-Path -LiteralPath $TestRoot -PathType Container)) {
                throw "Diagnostic directory does not exist."
            }

            $items = @(
                Get-ChildItem `
                    -LiteralPath $TestRoot `
                    -File `
                    -Recurse `
                    -ErrorAction Stop |
                    ForEach-Object {

                        [System.IO.Path]::GetRelativePath(
                            $TestRoot,
                            $_.FullName
                        )
                    }
            )

            if ($items.Count -eq 0) {
                return "(no files)"
            }

            return ($items -join "`n")
        }


        # ----------------------------------------------------
        # REPLACE TEXT
        # ----------------------------------------------------

        "replace_text" {

            if (
                $null -eq $Arguments -or
                [string]::IsNullOrWhiteSpace(
                    [string]$Arguments.path
                )
            ) {
                throw "replace_text requires a non-empty path"
            }

            $path = Resolve-AgentTestPath `
                -Root $TestRoot `
                -RelativePath ([string]$Arguments.path)

            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "File does not exist: $($Arguments.path)"
            }

            $content = Get-Content `
                -LiteralPath $path `
                -Raw `
                -ErrorAction Stop

            $oldText = [string]$Arguments.old_text
            $newText = [string]$Arguments.new_text

            if (-not $content.Contains($oldText)) {
                throw "old_text was not found"
            }

            $content = $content.Replace(
                $oldText,
                $newText
            )

            Set-Content `
                -LiteralPath $path `
                -Value $content `
                -NoNewline `
                -ErrorAction Stop

            return "SUCCESS: replacement completed"
        }


        # ----------------------------------------------------
        # SHELL COMMAND
        # ----------------------------------------------------

        "run_command" {

            if ($null -eq $Arguments) {
                throw "run_command requires arguments"
            }

            $command = [string]$Arguments.command

            $allowed = @(
                "git status --short",
                "git rev-parse --show-toplevel",
                "dotnet --version"
            )

            if ($command -notin $allowed) {
                throw "Command rejected by diagnostic whitelist: $command"
            }

            Push-Location $RepoRoot

            try {

                $output = & cmd.exe /c $command 2>&1 |
                    Out-String
            }
            finally {

                Pop-Location
            }

            return $output.Trim()
        }


        # ----------------------------------------------------
        # REAL INTERNET FETCH
        # ----------------------------------------------------

        "web_fetch" {

            if (
                $null -eq $Arguments -or
                [string]::IsNullOrWhiteSpace(
                    [string]$Arguments.url
                )
            ) {
                throw "web_fetch requires a URL"
            }

            $uri = [Uri]([string]$Arguments.url)

            if (
                $uri.Scheme -ne "https" -or
                $uri.Host -notin @(
                    "example.com",
                    "www.example.com"
                )
            ) {
                throw "Diagnostic web_fetch only allows https://example.com/"
            }

            $response = Invoke-WebRequest `
                -Uri $uri.AbsoluteUri `
                -UseBasicParsing `
                -TimeoutSec 20 `
                -ErrorAction Stop

            $text = [string]$response.Content

            if ($text.Length -gt 4000) {
                $text = $text.Substring(0, 4000)
            }

            return @"
HTTP $($response.StatusCode)

$text
"@
        }


        # ----------------------------------------------------
        # REAL INTERNET SEARCH
        # ----------------------------------------------------

        "web_search" {

            if (
                $null -eq $Arguments -or
                [string]::IsNullOrWhiteSpace(
                    [string]$Arguments.query
                )
            ) {
                throw "web_search requires a query"
            }

            $query = [Uri]::EscapeDataString(
                [string]$Arguments.query
            )

            $url = "https://www.bing.com/search?format=rss&q=$query"

            $response = Invoke-WebRequest `
                -Uri $url `
                -UseBasicParsing `
                -TimeoutSec 20 `
                -ErrorAction Stop

            [xml]$xml = $response.Content

            $items = @(
                $xml.rss.channel.item
            ) |
                Select-Object -First 5

            if (-not $items) {
                throw "Search returned no results"
            }

            $result = @(
                $items |
                    ForEach-Object {
                        "$($_.title) | $($_.link)"
                    }
            )

            return ($result -join "`n")
        }


        # ----------------------------------------------------
        # UNKNOWN TOOL
        # ----------------------------------------------------

        default {

            throw "Unknown tool: $Name"
        }
    }
}


# ============================================================
# GENERIC MULTI-TURN AGENT LOOP
# ============================================================

function Invoke-OllamaAgentScenario {
    param(
        [Parameter(Mandatory)]
        [string]$Model,

        [Parameter(Mandatory)]
        [string]$Prompt,

        [Parameter(Mandatory)]
        [string]$TestRoot,

        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [int]$MaxTurns = 8
    )

    $tools = Get-AgentTestTools

    $messages = @(
        @{
            role = "user"
            content = $Prompt
        }
    )

    $calls = @()
    $results = @()
    $turnLog = @()

    $finalText = ""

    for ($turn = 1; $turn -le $MaxTurns; $turn++) {

        $body = @{
            model = $Model

            messages = $messages

            tools = $tools

            stream = $false

            options = @{
                temperature = 0
            }
        } | ConvertTo-Json -Depth 40


        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        $response = Invoke-RestMethod `
            -Uri "http://127.0.0.1:11434/api/chat" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec 120

        $sw.Stop()


        $messages += $response.message


        # ----------------------------------------------------
        # IMPORTANT NULL FIX
        #
        # @($null).Count == 1 in PowerShell.
        #
        # Therefore explicitly handle null before turning
        # tool_calls into an array.
        # ----------------------------------------------------

        if ($null -eq $response.message.tool_calls) {

            $toolCalls = @()
        }
        else {

            $toolCalls = @(
                $response.message.tool_calls |
                    Where-Object {

                        $null -ne $_ -and
                        $null -ne $_.function -and
                        -not [string]::IsNullOrWhiteSpace(
                            [string]$_.function.name
                        )
                    }
            )
        }


        $toolNames = @(
            $toolCalls |
                ForEach-Object {
                    [string]$_.function.name
                }
        )


        $turnLog += [PSCustomObject]@{
            Turn = $turn

            ToolCalls = (
                $toolNames -join ", "
            )

            Content = [string]$response.message.content

            PromptTokens = $response.prompt_eval_count

            CachedPromptTokens = $response.prompt_eval_cached_count

            OutputTokens = $response.eval_count

            Seconds = [Math]::Round(
                $sw.Elapsed.TotalSeconds,
                3
            )
        }


        # ----------------------------------------------------
        # NO TOOLS = NORMAL ASSISTANT COMPLETION
        # ----------------------------------------------------

        if ($toolCalls.Count -eq 0) {

            $finalText = [string]$response.message.content

            break
        }


        # ----------------------------------------------------
        # EXECUTE REQUESTED TOOLS
        # ----------------------------------------------------

        foreach ($call in $toolCalls) {

            $name = [string]$call.function.name

            $arguments = $call.function.arguments

            $calls += $name


            try {

                $toolResult = Invoke-AgentTestTool `
                    -Name $name `
                    -Arguments $arguments `
                    -TestRoot $TestRoot `
                    -RepoRoot $RepoRoot
            }
            catch {

                $toolResult = "ERROR: $($_.Exception.Message)"
            }


            $argumentJson = ""

            if ($null -ne $arguments) {

                try {

                    $argumentJson = (
                        $arguments |
                            ConvertTo-Json `
                                -Compress `
                                -Depth 10
                    )
                }
                catch {

                    $argumentJson = [string]$arguments
                }
            }


            $results += [PSCustomObject]@{
                Tool = $name
                Arguments = $argumentJson
                Result = [string]$toolResult
            }


            $messages += @{
                role = "tool"

                tool_name = $name

                content = [string]$toolResult
            }
        }
    }


    return [PSCustomObject]@{
        Calls = @($calls)

        Results = @($results)

        Final = $finalText

        Turns = @($turnLog)
    }
}


# ============================================================
# BASIC TOOLS + RESPONSES API + SPEED TEST
# ============================================================

function Test-AgentTools {
    param(
        [Parameter(Mandatory)]
        [string]$Model
    )

    Write-Host ""
    Write-Host "========================================"
    Write-Host "TESTING: $Model"
    Write-Host "========================================"


    # ========================================================
    # TEST 1
    # Native /api/chat structured tool call
    # ========================================================

    $chatBody = @{
        model = $Model

        messages = @(
            @{
                role = "user"

                content = @"
Use the write_file tool to write exactly 'hello from agent' to test.txt.
Do not answer normally.
"@
            }
        )

        tools = @(
            @{
                type = "function"

                function = @{
                    name = "write_file"

                    description = "Writes text to a file"

                    parameters = @{
                        type = "object"

                        properties = @{
                            path = @{
                                type = "string"
                            }

                            content = @{
                                type = "string"
                            }
                        }

                        required = @(
                            "path",
                            "content"
                        )

                        additionalProperties = $false
                    }
                }
            }
        )

        stream = $false
    } | ConvertTo-Json -Depth 20


    $wall1 = [System.Diagnostics.Stopwatch]::StartNew()

    $r1 = Invoke-RestMethod `
        -Uri "http://127.0.0.1:11434/api/chat" `
        -Method Post `
        -ContentType "application/json" `
        -Body $chatBody `
        -TimeoutSec 120

    $wall1.Stop()


    Write-Host ""
    Write-Host "--- TEST 1: /api/chat TOOL CALL ---"

    $r1 |
        ConvertTo-Json -Depth 30


    $promptSeconds = (
        [double]$r1.prompt_eval_duration / 1e9
    )

    $evalSeconds = (
        [double]$r1.eval_duration / 1e9
    )

    $loadSeconds = (
        [double]$r1.load_duration / 1e9
    )

    $totalSeconds = (
        [double]$r1.total_duration / 1e9
    )


    if ($promptSeconds -gt 0) {

        $promptTPS = (
            [double]$r1.prompt_eval_count /
            $promptSeconds
        )
    }
    else {

        $promptTPS = 0
    }


    if ($evalSeconds -gt 0) {

        $generationTPS = (
            [double]$r1.eval_count /
            $evalSeconds
        )
    }
    else {

        $generationTPS = 0
    }


    Write-Host ""
    Write-Host "--- TEST 1 SPEED ---"

    Write-Host (
        "Load time:       {0:N3} s" -f
        $loadSeconds
    )

    Write-Host (
        "Prompt tokens:   {0}" -f
        $r1.prompt_eval_count
    )

    Write-Host (
        "Cached tokens:   {0}" -f
        $r1.prompt_eval_cached_count
    )

    Write-Host (
        "Prompt speed:    {0:N2} tok/s" -f
        $promptTPS
    )

    Write-Host (
        "Output tokens:   {0}" -f
        $r1.eval_count
    )

    Write-Host (
        "Generation:      {0:N2} tok/s" -f
        $generationTPS
    )

    Write-Host (
        "Ollama total:    {0:N3} s" -f
        $totalSeconds
    )

    Write-Host (
        "Wall time:       {0:N3} s" -f
        $wall1.Elapsed.TotalSeconds
    )


    # ========================================================
    # TEST 2
    # OpenAI Responses API tool-result continuation
    # ========================================================

    $responseTools = @(
        @{
            type = "function"

            name = "write_file"

            description = "Writes text to a file"

            parameters = @{
                type = "object"

                properties = @{
                    path = @{
                        type = "string"
                    }

                    content = @{
                        type = "string"
                    }
                }

                required = @(
                    "path",
                    "content"
                )

                additionalProperties = $false
            }
        }
    )


    $responseBody = @{
        model = $Model

        input = @(
            @{
                role = "user"

                content = @"
Use the write_file tool to write exactly 'hello from agent' to test.txt.
After the tool succeeds, reply exactly DONE.
"@
            }

            @{
                type = "function_call"

                id = "fc_test_1"

                call_id = "call_test_1"

                name = "write_file"

                arguments = '{"path":"test.txt","content":"hello from agent"}'
            }

            @{
                type = "function_call_output"

                call_id = "call_test_1"

                output = "File written successfully."
            }
        )

        tools = $responseTools

        stream = $false
    } | ConvertTo-Json -Depth 20


    $wall2 = [System.Diagnostics.Stopwatch]::StartNew()

    $r2 = Invoke-RestMethod `
        -Uri "http://127.0.0.1:11434/v1/responses" `
        -Method Post `
        -ContentType "application/json" `
        -Body $responseBody `
        -TimeoutSec 120

    $wall2.Stop()


    Write-Host ""
    Write-Host "--- TEST 2: /v1/responses TOOL LOOP ---"

    $r2 |
        ConvertTo-Json -Depth 30


    Write-Host ""
    Write-Host "--- TEST 2 SPEED ---"

    Write-Host (
        "Input tokens:    {0}" -f
        $r2.usage.input_tokens
    )

    Write-Host (
        "Output tokens:   {0}" -f
        $r2.usage.output_tokens
    )

    Write-Host (
        "Wall time:       {0:N3} s" -f
        $wall2.Elapsed.TotalSeconds
    )


    # ========================================================
    # TEST 3
    # Longer raw generation-speed benchmark
    # ========================================================

    $speedBody = @{
        model = $Model

        messages = @(
            @{
                role = "user"

                content = @"
Explain how a TCP connection works from connection establishment through data transfer and connection termination.

Be technically detailed and keep writing until you have produced a substantial answer.
"@
            }
        )

        options = @{
            temperature = 0

            num_predict = 256
        }

        stream = $false
    } | ConvertTo-Json -Depth 20


    $wall3 = [System.Diagnostics.Stopwatch]::StartNew()

    $r3 = Invoke-RestMethod `
        -Uri "http://127.0.0.1:11434/api/chat" `
        -Method Post `
        -ContentType "application/json" `
        -Body $speedBody `
        -TimeoutSec 120

    $wall3.Stop()


    $promptSeconds3 = (
        [double]$r3.prompt_eval_duration / 1e9
    )

    $evalSeconds3 = (
        [double]$r3.eval_duration / 1e9
    )

    $loadSeconds3 = (
        [double]$r3.load_duration / 1e9
    )

    $totalSeconds3 = (
        [double]$r3.total_duration / 1e9
    )


    if ($promptSeconds3 -gt 0) {

        $promptTPS3 = (
            [double]$r3.prompt_eval_count /
            $promptSeconds3
        )
    }
    else {

        $promptTPS3 = 0
    }


    if ($evalSeconds3 -gt 0) {

        $generationTPS3 = (
            [double]$r3.eval_count /
            $evalSeconds3
        )
    }
    else {

        $generationTPS3 = 0
    }


    Write-Host ""
    Write-Host "--- TEST 3: GENERATION SPEED ---"

    Write-Host (
        "Prompt tokens:   {0}" -f
        $r3.prompt_eval_count
    )

    Write-Host (
        "Cached tokens:   {0}" -f
        $r3.prompt_eval_cached_count
    )

    Write-Host (
        "Prompt speed:    {0:N2} tok/s" -f
        $promptTPS3
    )

    Write-Host (
        "Output tokens:   {0}" -f
        $r3.eval_count
    )

    Write-Host (
        "Generation:      {0:N2} tok/s" -f
        $generationTPS3
    )

    Write-Host (
        "Generation time: {0:N3} s" -f
        $evalSeconds3
    )

    Write-Host (
        "Load time:       {0:N3} s" -f
        $loadSeconds3
    )

    Write-Host (
        "Ollama total:    {0:N3} s" -f
        $totalSeconds3
    )

    Write-Host (
        "Wall time:       {0:N3} s" -f
        $wall3.Elapsed.TotalSeconds
    )


    Write-Host ""
    Write-Host "========================================"
    Write-Host "SUMMARY: $Model"
    Write-Host "========================================"

    Write-Host (
        "Tool generation: {0:N2} tok/s" -f
        $generationTPS
    )

    Write-Host (
        "Long generation: {0:N2} tok/s" -f
        $generationTPS3
    )

    Write-Host (
        "Prompt eval:     {0:N2} tok/s" -f
        $promptTPS3
    )

    Write-Host "========================================"
}


# ============================================================
# CAPABILITY / AGENT BEHAVIOR TESTS
# ============================================================

function Test-AgentCapabilities {
    param(
        [Parameter(Mandatory)]
        [string]$Model
    )

    $repoRoot = (Get-Location).Path

    $testRoot = Join-Path `
        $env:TEMP `
        "ollama-agent-capability-test"


    # --------------------------------------------------------
    # RESET SANDBOX
    # --------------------------------------------------------

    Remove-Item `
        -LiteralPath $testRoot `
        -Recurse `
        -Force `
        -ErrorAction SilentlyContinue

    New-Item `
        -ItemType Directory `
        -Path $testRoot `
        -Force `
        -ErrorAction Stop |
        Out-Null


    Set-Content `
        -LiteralPath (Join-Path $testRoot "alpha.txt") `
        -Value "ALPHA_MARKER_42" `
        -NoNewline `
        -ErrorAction Stop


    Set-Content `
        -LiteralPath (Join-Path $testRoot "edit_me.cs") `
        -Value 'public int Value() => 1;' `
        -NoNewline `
        -ErrorAction Stop


    Write-Host ""
    Write-Host "========================================"
    Write-Host "CAPABILITY TEST: $Model"
    Write-Host "========================================"


    # ========================================================
    # HOST INTERNET PREFLIGHT
    # ========================================================

    try {

        $net = Invoke-WebRequest `
            -Uri "https://example.com/" `
            -UseBasicParsing `
            -TimeoutSec 15 `
            -ErrorAction Stop

        $networkPreflight = (
            $net.StatusCode -eq 200
        )
    }
    catch {

        $networkPreflight = $false
    }


    Write-Host ""
    Write-Host "Host internet:"
    Write-Host $networkPreflight


    # ========================================================
    # TEST 1
    # WRITE FILE
    # ========================================================

    $write = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
You must use write_file.

Write exactly:

HELLO_TOOL_99

into result.txt.

Do not claim success unless the tool succeeds.
"@


    $writePath = Join-Path `
        $testRoot `
        "result.txt"


    $writePass = (
        $write.Calls -contains "write_file"
    ) -and (
        Test-Path `
            -LiteralPath $writePath `
            -PathType Leaf
    ) -and (
        (Get-Content `
            -LiteralPath $writePath `
            -Raw `
            -ErrorAction SilentlyContinue
        ) -eq "HELLO_TOOL_99"
    )


    # ========================================================
    # TEST 2
    # SHELL COMMAND
    # ========================================================

    $command = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
Use run_command with exactly:

git status --short

Report the result only after the tool executes.
"@


    $commandPass = (
        $command.Calls -contains "run_command"
    )


    # ========================================================
    # TEST 3
    # REAL HTTP FETCH
    # ========================================================

    $fetch = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
Use web_fetch to fetch exactly:

https://example.com/

Then tell me the page title.

You must use web_fetch.
"@


    $fetchToolResult = (
        $fetch.Results |
            Where-Object {
                $_.Tool -eq "web_fetch"
            } |
            Select-Object -Last 1
    ).Result


    $fetchPass = (
        $fetch.Calls -contains "web_fetch"
    ) -and (
        [string]$fetchToolResult -match "Example Domain"
    )


    # ========================================================
    # TEST 4
    # REAL WEB SEARCH
    # ========================================================

    $search = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
Use web_search to search for:

Ollama official documentation

Report the title of one returned result.

You must use web_search.
"@


    $searchToolResult = (
        $search.Results |
            Where-Object {
                $_.Tool -eq "web_search"
            } |
            Select-Object -Last 1
    ).Result


    $searchPass = (
        $search.Calls -contains "web_search"
    ) -and (
        -not [string]::IsNullOrWhiteSpace(
            [string]$searchToolResult
        )
    ) -and (
        [string]$searchToolResult -notmatch "^ERROR:"
    )


    # ========================================================
    # TEST 5
    # MULTI-STEP TOOL CHAIN
    # ========================================================

    $chain = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
Complete these steps using tools:

1. Call list_files.
2. Read alpha.txt.
3. Take the exact marker from alpha.txt.
4. Write that exact marker into copied.txt.
5. Read copied.txt to verify it.
6. Tell me the verified marker.

Do not skip the required tool calls.
"@


    $copiedPath = Join-Path `
        $testRoot `
        "copied.txt"


    $chainReadCount = @(
        $chain.Calls |
            Where-Object {
                $_ -eq "read_file"
            }
    ).Count


    $chainPass = (
        $chain.Calls -contains "list_files"
    ) -and (
        $chain.Calls -contains "write_file"
    ) -and (
        $chainReadCount -ge 2
    ) -and (
        Test-Path `
            -LiteralPath $copiedPath `
            -PathType Leaf
    ) -and (
        (Get-Content `
            -LiteralPath $copiedPath `
            -Raw `
            -ErrorAction SilentlyContinue
        ) -eq "ALPHA_MARKER_42"
    )


    # ========================================================
    # TEST 6
    # EDIT + VERIFY
    # ========================================================

    $edit = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
Use replace_text on edit_me.cs.

Replace exactly:

=> 1;

with:

=> 2;

Then use read_file to verify the edit.
"@


    $editedPath = Join-Path `
        $testRoot `
        "edit_me.cs"


    $edited = Get-Content `
        -LiteralPath $editedPath `
        -Raw `
        -ErrorAction Stop


    $editPass = (
        $edit.Calls -contains "replace_text"
    ) -and (
        $edit.Calls -contains "read_file"
    ) -and (
        $edited -match "=> 2;"
    )


    # ========================================================
    # TEST 7
    # ERROR RECOVERY
    # ========================================================

    $recovery = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
First use read_file on missing.txt.

That file does not exist.

When the tool reports the error:

1. Call list_files.
2. Read alpha.txt.
3. Report the exact contents of alpha.txt.

Do not repeatedly retry missing.txt after receiving the error.
"@


    $missingReads = @(
        $recovery.Results |
            Where-Object {
                $_.Tool -eq "read_file" -and
                $_.Arguments -match "missing\.txt"
            }
    ).Count


    $alphaReads = @(
        $recovery.Results |
            Where-Object {
                $_.Tool -eq "read_file" -and
                $_.Arguments -match "alpha\.txt"
            }
    ).Count


    $recoveryPass = (
        $missingReads -ge 1
    ) -and (
        $recovery.Calls -contains "list_files"
    ) -and (
        $alphaReads -ge 1
    ) -and (
        $recovery.Final -match "ALPHA_MARKER_42"
    )


    # ========================================================
    # TEST 8
    # TOOL RESTRAINT
    # ========================================================

    $restraint = Invoke-OllamaAgentScenario `
        -Model $Model `
        -TestRoot $testRoot `
        -RepoRoot $repoRoot `
        -Prompt @"
Do not use any tools.

What is 2 + 2?

Reply exactly:

4
"@


    $restraintPass = (
        $restraint.Calls.Count -eq 0
    ) -and (
        $restraint.Final.Trim() -eq "4"
    )


    # ========================================================
    # SUMMARY
    # ========================================================

    $summary = @(

        [PSCustomObject]@{
            Test = "Host internet"
            Pass = $networkPreflight
            Calls = "-"
        }

        [PSCustomObject]@{
            Test = "Write file"
            Pass = $writePass
            Calls = ($write.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "Shell command"
            Pass = $commandPass
            Calls = ($command.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "HTTP fetch"
            Pass = $fetchPass
            Calls = ($fetch.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "Web search"
            Pass = $searchPass
            Calls = ($search.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "Multi-step chain"
            Pass = $chainPass
            Calls = ($chain.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "Edit + verify"
            Pass = $editPass
            Calls = ($edit.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "Error recovery"
            Pass = $recoveryPass
            Calls = ($recovery.Calls -join ", ")
        }

        [PSCustomObject]@{
            Test = "Tool restraint"
            Pass = $restraintPass
            Calls = ($restraint.Calls -join ", ")
        }
    )


    Write-Host ""
    Write-Host "========================================"
    Write-Host "RESULTS: $Model"
    Write-Host "========================================"

    $summary |
        Format-Table -AutoSize


    Write-Host ""
    Write-Host "Diagnostic directory:"
    Write-Host $testRoot


    # --------------------------------------------------------
    # DETAILED FAILURE INFO
    # --------------------------------------------------------

    $scenarios = @{

        "Write file" = $write

        "Shell command" = $command

        "HTTP fetch" = $fetch

        "Web search" = $search

        "Multi-step chain" = $chain

        "Edit + verify" = $edit

        "Error recovery" = $recovery

        "Tool restraint" = $restraint
    }


    foreach ($row in $summary) {

        if (
            $row.Test -ne "Host internet" -and
            -not $row.Pass
        ) {

            Write-Host ""
            Write-Host "----------------------------------------"
            Write-Host "FAILED: $($row.Test)"
            Write-Host "----------------------------------------"

            $scenario = $scenarios[$row.Test]

            Write-Host ""
            Write-Host "Turns:"

            $scenario.Turns |
                Format-Table -AutoSize

            Write-Host ""
            Write-Host "Tool results:"

            $scenario.Results |
                Format-Table -Wrap -AutoSize

            Write-Host ""
            Write-Host "Final response:"

            Write-Host $scenario.Final
        }
    }


    return $summary
}


# ============================================================
# FULL TEST SUITE
# ============================================================

function Test-AgentFull {
    param(
        [Parameter(Mandatory)]
        [string]$Model
    )

    Test-AgentTools $Model

    Test-AgentCapabilities $Model
}


# ============================================================
# TEST MULTIPLE MODELS
# ============================================================

function Test-AllAgentModels {
    param(
        [string[]]$Models = @(
            "llama3.1:8b",
            "hermes3:8b",
            "granite3.3:8b"
        )
    )

    foreach ($model in $Models) {

        Write-Host ""
        Write-Host ""
        Write-Host "########################################"
        Write-Host "# MODEL: $model"
        Write-Host "########################################"

        Test-AgentFull $model
    }
}