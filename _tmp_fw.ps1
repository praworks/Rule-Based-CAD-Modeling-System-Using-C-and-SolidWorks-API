$asm=[reflection.assembly]::LoadFrom("d:\SolidWorks Project\Rule-Based-CAD-Modeling-System-Using-C-and-SolidWorks-API\References\SolidWorks.Interop.sldworks.dll")
$t=$asm.GetType("SolidWorks.Interop.sldworks.IModelDoc2")
$methods=$t.GetMethods() | Where-Object { $_.Name -like "FeatureFillet*" }
foreach($m in $methods){
  $sig = ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', '
  Write-Output "$($m.ReturnType.Name) $($m.Name) params=$($m.GetParameters().Count): $sig"
}
