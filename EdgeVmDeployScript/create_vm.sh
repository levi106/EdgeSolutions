#!/bin/bash

suffix=`date +"%Y%m%d"`
rand=$RANDOM
resourceGroup="edge-rg-$suffix"
deploymentName="deploy-iotedge-$suffix"
location="japaneast"
virtualMachineName="edge-$suffix"
iotHubName="hubdemo42"

az iot hub device-identity delete --device-id ${virtualMachineName} --hub-name ${iotHubName}
az group create --name ${resourceGroup} --location ${location}
az vm image accept-terms --urn microsoft_iot_edge:iot_edge_vm_ubuntu:ubuntu_1604_edgeruntimeonly:latest
az group deployment create -n ${deploymentName} -g ${resourceGroup} --template-file "./azuredeploy.json" --parameters "{\"virtualMachineName\": {\"value\": \"${virtualMachineName}\"}}"
az iot hub device-identity create --hub-name ${iotHubName} --device-id ${virtualMachineName} --edge-enabled
connectionString=`az iot hub device-identity show-connection-string --device-id ${virtualMachineName} --hub-name ${iotHubName} | jq -r .connectionString`
echo {\"deploymentName\": \"${deploymentName}\", \"resourceGroup\": \"${resourceGroup}\", \"iotHubName\": \"${iotHubName}\", \"virtualMachineName\": \"${virtualMachineName}\", \"connectionString\": \"${connectionString}\"} > result.json
az vm run-command invoke -g ${resourceGroup} -n ${virtualMachineName} --command-id RunShellScript --script "/etc/iotedge/configedge.sh '${connectionString}'"
jq . result.json
