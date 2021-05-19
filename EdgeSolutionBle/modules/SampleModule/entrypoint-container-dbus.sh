#!/bin/bash

echo "starting dbus"
/etc/init.d/dbus start

echo "start bluetooth daemon"
/usr/lib/bluetooth/bluetoothd -d &

cd /app
dotnet SampleModule.dll

exit 0