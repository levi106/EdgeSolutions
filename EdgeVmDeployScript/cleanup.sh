virtualMachineName=`jq -r  .virtualMachineName result.json`
iotHubName=`jq -r .iotHubName result.json`
deploymentName=`jq -r .deploymentName result.json`
resourceGroup=`jq -r .resourceGroup result.json`

az iot hub device-identity delete --device-id ${virtualMachineName} --hub-name ${iotHubName}
# az group deployment delete --name ${deploymentName} --resource-group ${resourceGroup}
az group delete -g ${resourceGroup} -y
