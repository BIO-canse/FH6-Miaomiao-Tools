param(
    [switch]$SelfCheck
)

$ErrorActionPreference = "Stop"
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Runtime.WindowsRuntime
[Windows.Media.Ocr.OcrEngine, Windows.Media.Ocr, ContentType = WindowsRuntime] | Out-Null
[Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapPixelFormat, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapAlphaMode, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapTransform, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.ExifOrientationMode, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.ColorManagementMode, Windows.Graphics.Imaging, ContentType = WindowsRuntime] | Out-Null
[Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime] | Out-Null

$script:AsTaskOneGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq "AsTask" -and $_.IsGenericMethod -and $_.GetGenericArguments().Length -eq 1 -and $_.GetParameters().Length -eq 1 } |
    Select-Object -First 1
$script:AsTaskTwoGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq "AsTask" -and $_.IsGenericMethod -and $_.GetGenericArguments().Length -eq 2 -and $_.GetParameters().Length -eq 1 } |
    Select-Object -First 1

function Write-BridgeJson {
    param([object]$Value)
    $json = $Value | ConvertTo-Json -Compress -Depth 10
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function Wait-AsyncOperation {
    param(
        [object]$Operation,
        [Type]$ResultType
    )
    $task = $script:AsTaskOneGeneric.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $task.Wait()
    return $task.Result
}

function Wait-AsyncOperationWithProgress {
    param(
        [object]$Operation,
        [Type]$ResultType,
        [Type]$ProgressType
    )
    $task = $script:AsTaskTwoGeneric.MakeGenericMethod($ResultType, $ProgressType).Invoke($null, @($Operation))
    $task.Wait()
    return $task.Result
}

function Get-LanguageNeeds {
    $value = [string]$env:FH6_MEDIAOCR_LANGUAGES
    if ([string]::IsNullOrWhiteSpace($value)) {
        return @{ Chinese = $true; English = $true }
    }
    $lower = $value.ToLowerInvariant()
    $needChinese = $lower.Contains("chi") -or $lower.Contains("zh") -or $lower.Contains("中文")
    $needEnglish = $lower.Contains("eng") -or $lower.Contains("en") -or $lower.Contains("english")
    if (-not $needChinese -and -not $needEnglish) {
        $needChinese = $true
        $needEnglish = $true
    }
    return @{ Chinese = $needChinese; English = $needEnglish }
}

function Select-LanguageTag {
    param(
        [string[]]$AvailableTags,
        [bool]$Chinese
    )
    $preferred = if ($Chinese) {
        @("zh-Hans-CN", "zh-CN", "zh-Hans", "zh")
    }
    else {
        @("en-US", "en-GB", "en")
    }

    foreach ($tag in $preferred) {
        foreach ($available in $AvailableTags) {
            if ([string]::Equals($available, $tag, [StringComparison]::OrdinalIgnoreCase)) {
                return $available
            }
        }
    }
    foreach ($tag in $preferred) {
        foreach ($available in $AvailableTags) {
            if ($available.StartsWith($tag, [StringComparison]::OrdinalIgnoreCase)) {
                return $available
            }
        }
    }
    return $null
}

function New-MediaEngines {
    $needs = Get-LanguageNeeds
    $availableTags = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | ForEach-Object { $_.LanguageTag })
    $selected = New-Object System.Collections.Generic.List[object]

    if ($needs.Chinese) {
        $tag = Select-LanguageTag -AvailableTags $availableTags -Chinese $true
        if ([string]::IsNullOrWhiteSpace($tag)) {
            throw "MediaOCR 缺少中文 OCR 语言。available_languages=$($availableTags -join ',')"
        }
        $language = [Windows.Globalization.Language]::new($tag)
        $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
        if ($null -eq $engine) {
            throw "MediaOCR 无法创建中文 OCR engine：$tag"
        }
        $selected.Add([pscustomobject]@{ Tag = $tag; Engine = $engine }) | Out-Null
    }

    if ($needs.English) {
        $tag = Select-LanguageTag -AvailableTags $availableTags -Chinese $false
        if ([string]::IsNullOrWhiteSpace($tag)) {
            throw "MediaOCR 缺少英文 OCR 语言。available_languages=$($availableTags -join ',')"
        }
        $already = $false
        foreach ($entry in $selected) {
            if ([string]::Equals($entry.Tag, $tag, [StringComparison]::OrdinalIgnoreCase)) {
                $already = $true
            }
        }
        if (-not $already) {
            $language = [Windows.Globalization.Language]::new($tag)
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
            if ($null -eq $engine) {
                throw "MediaOCR 无法创建英文 OCR engine：$tag"
            }
            $selected.Add([pscustomobject]@{ Tag = $tag; Engine = $engine }) | Out-Null
        }
    }

    return ,$selected.ToArray()
}

function Get-RequestedScale {
    $requested = 1.0
    if (-not [string]::IsNullOrWhiteSpace($env:FH6_MEDIAOCR_SCALE)) {
        [double]::TryParse(
            $env:FH6_MEDIAOCR_SCALE,
            [System.Globalization.NumberStyles]::Any,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [ref]$requested) | Out-Null
    }
    if ($requested -le 0) {
        $requested = 1.0
    }
    return $requested
}

function Decode-Image {
    param([string]$Base64)

    $bytes = [Convert]::FromBase64String($Base64)
    $buffer = [System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions]::AsBuffer($bytes)
    $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
    Wait-AsyncOperationWithProgress -Operation $stream.WriteAsync($buffer) -ResultType ([UInt32]) -ProgressType ([UInt32]) | Out-Null
    $stream.Seek(0)

    $decoder = Wait-AsyncOperation -Operation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) -ResultType ([Windows.Graphics.Imaging.BitmapDecoder])
    $sourceWidth = [double]$decoder.PixelWidth
    $sourceHeight = [double]$decoder.PixelHeight
    $maxDimension = [double][Windows.Media.Ocr.OcrEngine]::MaxImageDimension
    if ($maxDimension -le 0) {
        $maxDimension = 2600.0
    }
    $requestedScale = Get-RequestedScale
    $maxScale = [Math]::Min($maxDimension / [Math]::Max(1.0, $sourceWidth), $maxDimension / [Math]::Max(1.0, $sourceHeight))
    $scale = [Math]::Max(0.1, [Math]::Min($requestedScale, $maxScale))
    if ($scale -lt 1.0) {
        $scale = $maxScale
    }

    $targetWidth = [Math]::Max(1, [int][Math]::Round($sourceWidth * $scale))
    $targetHeight = [Math]::Max(1, [int][Math]::Round($sourceHeight * $scale))

    $transform = [Windows.Graphics.Imaging.BitmapTransform]::new()
    $transform.ScaledWidth = [UInt32]$targetWidth
    $transform.ScaledHeight = [UInt32]$targetHeight

    $bitmap = Wait-AsyncOperation -Operation $decoder.GetSoftwareBitmapAsync(
        [Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8,
        [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied,
        $transform,
        [Windows.Graphics.Imaging.ExifOrientationMode]::IgnoreExifOrientation,
        [Windows.Graphics.Imaging.ColorManagementMode]::DoNotColorManage) -ResultType ([Windows.Graphics.Imaging.SoftwareBitmap])

    return [pscustomobject]@{
        Bitmap = $bitmap
        Scale = $scale
        SourceWidth = [int]$sourceWidth
        SourceHeight = [int]$sourceHeight
        Width = $targetWidth
        Height = $targetHeight
    }
}

function Invoke-MediaOcr {
    param(
        [object[]]$Engines,
        [string]$Base64
    )

    $decoded = Decode-Image -Base64 $Base64
    $items = New-Object System.Collections.Generic.List[object]
    $rawParts = New-Object System.Collections.Generic.List[string]

    foreach ($entry in $Engines) {
        $result = Wait-AsyncOperation -Operation $entry.Engine.RecognizeAsync($decoded.Bitmap) -ResultType ([Windows.Media.Ocr.OcrResult])
        $rawParts.Add("language=$($entry.Tag)") | Out-Null
        $rawParts.Add([string]$result.Text) | Out-Null
        foreach ($line in $result.Lines) {
            foreach ($word in $line.Words) {
                if ([string]::IsNullOrWhiteSpace($word.Text)) {
                    continue
                }
                $rect = $word.BoundingRect
                $items.Add([pscustomobject]@{
                    text = [string]$word.Text
                    confidence = 1.0
                    rect = @([double]$rect.X, [double]$rect.Y, [double]$rect.Width, [double]$rect.Height)
                    language = [string]$entry.Tag
                }) | Out-Null
            }
        }
    }

    return [pscustomobject]@{
        code = 100
        data = $items.ToArray()
        scale = [double]$decoded.Scale
        raw = ($rawParts -join "`n")
        languages = @($Engines | ForEach-Object { $_.Tag })
        image = @{
            source_width = $decoded.SourceWidth
            source_height = $decoded.SourceHeight
            width = $decoded.Width
            height = $decoded.Height
        }
    }
}

try {
    $engines = New-MediaEngines
    if ($SelfCheck) {
        Write-BridgeJson ([pscustomobject]@{
            code = 0
            engine = "MediaOCR"
            available_languages = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | ForEach-Object { $_.LanguageTag })
            selected_languages = @($engines | ForEach-Object { $_.Tag })
            max_image_dimension = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
            ocr_scale = Get-RequestedScale
        })
        exit 0
    }

    Write-BridgeJson ([pscustomobject]@{
        ready = $true
        engine = "MediaOCR"
        selected_languages = @($engines | ForEach-Object { $_.Tag })
        max_image_dimension = [Windows.Media.Ocr.OcrEngine]::MaxImageDimension
    })

    while (($line = [Console]::In.ReadLine()) -ne $null) {
        if ($line -eq "__exit__") {
            break
        }
        try {
            $request = $line | ConvertFrom-Json
            if ($null -eq $request.image_base64 -or [string]::IsNullOrWhiteSpace([string]$request.image_base64)) {
                throw "missing image_base64"
            }
            Write-BridgeJson (Invoke-MediaOcr -Engines $engines -Base64 ([string]$request.image_base64))
        }
        catch {
            Write-BridgeJson ([pscustomobject]@{
                code = 500
                error = $_.Exception.Message
                trace = $_.ScriptStackTrace
            })
        }
    }
}
catch {
    Write-BridgeJson ([pscustomobject]@{
        code = 500
        error = $_.Exception.Message
        trace = $_.ScriptStackTrace
    })
    exit 1
}
