#!/bin/bash

# --- Configuration Variables ---
# These variables should match the ones used in the deployScript.sh

# Azure Resource Group
RESOURCE_GROUP="alx-intro1-rg"

# --- Delete Azure Resources ---
echo "Deleting resource group: $RESOURCE_GROUP"
az group delete \
  --name "$RESOURCE_GROUP" \
  --yes \
  --no-wait

echo "Resource group deletion initiated. It might take a few minutes to complete."

