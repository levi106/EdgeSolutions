echo -e "o\nn\np\n1\n\n\nw" | fdisk /dev/sdc
partprobe
echo -e "y\n" | mkfs -t ext4 /dev/sdc1
mkdir /datadrive
mount /dev/sdc1 /datadrive
mkdir /datadrive/blob
chown -R 11000:11000 /datadrive/blob
chmod -R 700 /datadrive/blob
UUID=`blkid | grep sdc1 | sed -e "s/^\/dev\/sdc1: UUID=\"\([0-9a-z-]\+\)\" .\+$/\1/g"`
echo -e "UUID=${UUID}\t /datadrive\text4\tdefaults,nofail\t1\t2" >> /etc/fstab
curl https://packages.microsoft.com/config/ubuntu/16.04/multiarch/prod.list > ./microsoft-prod.list
cp microsoft-prod.list /etc/apt/sources.list.d/
apt update
apt upgrade -y
cat > /etc/docker/daemon.json << EOF
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  }
}
EOF
