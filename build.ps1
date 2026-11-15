# Hizli Aktar derleme: PC exe (Windows'taki .NET Framework csc) + APK (Gradle'siz: aapt2/javac/d8/apksigner)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out  = Join-Path $root 'cikti'
New-Item -ItemType Directory -Force $out | Out-Null

# ---- PC ----
# csc ve aapt2 bosluklu/Turkce karakterli yollarda sorun cikardigi icin ASCII gecici klasorde derle
$b = Join-Path $env:TEMP 'hizliaktar_build'
if (Test-Path $b) { Remove-Item -Recurse -Force $b }
New-Item -ItemType Directory -Force "$b\classes", "$b\dex" | Out-Null
Copy-Item -Recurse "$root\pc" "$b\pc"
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
Push-Location "$b\pc"
& $csc /nologo /codepage:65001 /optimize+ /target:winexe /out:HizliAktar.exe /win32icon:app.ico `
    /resource:logo_bg.png,logo_bg.png /resource:logo_small.png,logo_small.png /resource:qr_apk.png,qr_apk.png `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll Proto.cs MainForm.cs
$rc = $LASTEXITCODE
Pop-Location
if ($rc) { throw "PC derlemesi basarisiz" }
Copy-Item "$b\pc\HizliAktar.exe" "$out\HizliAktar.exe" -Force

# ---- Android ----
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" }
$bt  = "$sdk\build-tools\35.0.0"
$jar = "$sdk\platforms\android-35\android.jar"
Copy-Item "$root\android\AndroidManifest.xml" $b
Copy-Item -Recurse "$root\android\src" "$b\src"
Copy-Item -Recurse "$root\android\res" "$b\res"

& "$bt\aapt2.exe" compile --dir "$b\res" -o "$b\res.zip"
if ($LASTEXITCODE) { throw "aapt2 compile basarisiz" }
& "$bt\aapt2.exe" link -o "$b\base.apk" -I $jar --manifest "$b\AndroidManifest.xml" "$b\res.zip"
if ($LASTEXITCODE) { throw "aapt2 basarisiz" }
$srcs = Get-ChildItem -Recurse "$b\src" -Filter *.java | ForEach-Object FullName
& javac -nowarn -encoding UTF-8 -source 8 -target 8 -bootclasspath $jar -classpath "$bt\core-lambda-stubs.jar" -d "$b\classes" @srcs
if ($LASTEXITCODE) { throw "javac basarisiz" }
& jar cf "$b\classes.jar" -C "$b\classes" .
& "$bt\d8.bat" --release --min-api 24 --lib $jar --output "$b\dex" "$b\classes.jar"
if ($LASTEXITCODE) { throw "d8 basarisiz" }
& jar uf "$b\base.apk" -C "$b\dex" classes.dex
& "$bt\zipalign.exe" -f -p 4 "$b\base.apk" "$b\aligned.apk"
if ($LASTEXITCODE) { throw "zipalign basarisiz" }

# Ayni anahtarla imzalanirsa yeni surum eskisinin uzerine kurulur; bu dosyayi silmeyin.
$ks = "$root\android\hizliaktar.keystore"
if (-not (Test-Path $ks)) {
    & keytool -genkeypair -keystore $ks -storepass hizliaktar -keypass hizliaktar -alias ha `
        -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=HizliAktar" | Out-Null
}
Copy-Item $ks "$b\ks.keystore"
& "$bt\apksigner.bat" sign --ks "$b\ks.keystore" --ks-pass pass:hizliaktar --out "$b\HizliAktar.apk" "$b\aligned.apk"
if ($LASTEXITCODE) { throw "apksigner basarisiz" }
Copy-Item "$b\HizliAktar.apk" "$out\HizliAktar.apk" -Force
Write-Host "Tamam:" (Get-ChildItem $out | ForEach-Object { "$($_.Name) $([math]::Round($_.Length/1KB)) KB" })
