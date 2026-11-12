$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot
$images=@()
foreach($size in @(16,20,24,32,40,48,64,128,256)) {
    $bmp=New-Object Drawing.Bitmap $size,$size
    $g=[Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode='AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size/256.0,$size/256.0)
    $path=New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(8,8,104,104,180,90);$path.AddArc(144,8,104,104,270,90);$path.AddArc(144,144,104,104,0,90);$path.AddArc(8,144,104,104,90,90);$path.CloseFigure()
    $fill=New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#171c20'))
    $border=New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#bdd8c7')),8
    $g.FillPath($fill,$path);$g.DrawPath($border,$path)
    $stroke=New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#f4f7f5')),24
    $stroke.LineJoin='Round';$stroke.StartCap='Square';$stroke.EndCap='Square'
    $points=[Drawing.PointF[]]@([Drawing.PointF]::new(180,78),[Drawing.PointF]::new(100,78),[Drawing.PointF]::new(70,108),[Drawing.PointF]::new(70,164),[Drawing.PointF]::new(96,190),[Drawing.PointF]::new(180,190),[Drawing.PointF]::new(180,132),[Drawing.PointF]::new(130,132))
    $g.DrawLines($stroke,$points)
    $stream=New-Object IO.MemoryStream
    $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images+=,@{Size=$size;Bytes=$stream.ToArray()}
    if($size -eq 256){$bmp.Save((Join-Path $root 'assets/gpdt.png'),[Drawing.Imaging.ImageFormat]::Png)}
    $stream.Dispose();$g.Dispose();$bmp.Dispose();$path.Dispose();$fill.Dispose();$border.Dispose();$stroke.Dispose()
}
$file=[IO.File]::Create((Join-Path $root 'assets/gpdt.ico'))
$writer=New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach($item in $images){$dimension=if($item.Size -eq 256){0}else{$item.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$item.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$item.Bytes.Length}
    foreach($item in $images){$writer.Write([byte[]]$item.Bytes)}
} finally {$writer.Dispose();$file.Dispose()}
