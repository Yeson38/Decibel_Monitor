if (Test-Path -Path "./bin") {
    Remove-Item ./bin -recurse
}
dotnet publish Decibel_Monitor -c Release -p:CreateCipx=true
