if (Test-Path -Path "./bin") {
    Remove-Item ./bin -recurse
}
dotnet publish Decibel_Monitor.csproj -c Release -p:CreateCipx=true
