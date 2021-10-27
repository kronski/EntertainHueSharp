Write-Output "hej"
dotnet publish
scp -r bin/Debug/net5.0/publish/* client.json pi@raspberrypi:/home/pi/EntertainHueSharp
