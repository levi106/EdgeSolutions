#!/bin/bash

#pid = 0

#term_handler() {
#    if [ $pid -ne 0 ]; then
#        kill -SIGTERM "$pid"
#        wait "$pid"
#    fi
#
#    /etc/init.d/dbus stop
#
#    exit 143;
#}

#trap 'kill ${!}; term_handler' SIGINT SIGKILL SIGTERM SIGQUIT SIGTSTP SIGSTOP SIGHUP

echo "starting dbus"
/etc/init.d/dbus start

echo "start bluetooth daemon"
/usr/lib/bluetooth/bluetoothd -d &
#pid="$!"

cd /app
dotnet SampleModule.dll

exit 0