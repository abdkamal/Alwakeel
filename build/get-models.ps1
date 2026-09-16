# Fetches the local OCR and embedding models used in development and tests (git-ignored under tools/).
# Tesseract: tessdata_best ara + eng. Embeddings: multilingual-e5-small (ONNX int8) with its tokenizer files.
# Usage: pwsh build/get-models.ps1
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\tools' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $root) { $root = New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '..\tools') }
$tess = Join-Path $root 'tessdata'; $model = Join-Path $root 'models\multilingual-e5-small'
New-Item -ItemType Directory -Force -Path $tess, $model | Out-Null
$files = @(
  @{ url = 'https://github.com/tesseract-ocr/tessdata_best/raw/main/ara.traineddata'; out = Join-Path $tess 'ara.traineddata' },
  @{ url = 'https://github.com/tesseract-ocr/tessdata_best/raw/main/eng.traineddata'; out = Join-Path $tess 'eng.traineddata' },
  @{ url = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_quantized.onnx'; out = Join-Path $model 'model_quantized.onnx' },
  @{ url = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/tokenizer.json'; out = Join-Path $model 'tokenizer.json' },
  @{ url = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/tokenizer_config.json'; out = Join-Path $model 'tokenizer_config.json' },
  @{ url = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/config.json'; out = Join-Path $model 'config.json' },
  @{ url = 'https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/sentencepiece.bpe.model'; out = Join-Path $model 'sentencepiece.bpe.model' }
)
foreach ($f in $files) {
  if (Test-Path $f.out) { "exists  $($f.out)"; continue }
  "fetch   $($f.url)"
  & curl.exe -sSL --retry 5 --retry-delay 5 -C - -o $f.out $f.url
  if ($LASTEXITCODE -ne 0) { throw "download failed: $($f.url)" }
}
Get-ChildItem $tess, $model | Select-Object Name, Length | Format-Table -AutoSize
