#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Configuration Variables ---
# These variables are used throughout the script.
# It is recommended to modify them to match your environment.

# Azure Resource Group and Location
RESOURCE_GROUP="alx-intro1-rg"
LOCATION="eastus2"

# Azure Log Analytics Workspace for Container Apps environment
LOG_ANALYTICS_WORKSPACE="alx-analytics-workspace"

# Azure Container Registry (ACR) name
ACR_NAME="alxintro1"

# Azure Container Apps environment name
CONTAINERAPPS_ENVIRONMENT="alx-container-apps-environment"

# Application names
ORDER_SERVICE_APP_NAME="order-service"
SHIPPING_SERVICE_APP_NAME="shipping-service"
NOTIFICATION_SERVICE_APP_NAME="notification-service"

# Dapr component configuration
DAPR_PUBSUB_COMPONENT_NAME="pubsub"
DAPR_COMPONENT_YAML_FILE="dapr-inmemory-pubsub.yaml"


# --- 0. Build Local Docker Images ---
# This section builds the Docker images for each service from the local Dockerfiles.
echo "Building local Docker images..."
docker build -t order-service:latest ./OrderService
docker build -t shipping-service:latest ./ShippingService
docker build -t notification-service:latest ./NotificationService
echo "Docker images built successfully."


# --- 1. Create Azure Resources & Push Images ---
# This section creates the necessary Azure resources and pushes the Docker images to the Azure Container Registry.

echo "Creating resource group: $RESOURCE_GROUP"
az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION"

echo "Creating Azure Container Registry (ACR): $ACR_NAME"
az acr create \
  --resource-group "$RESOURCE_GROUP" \
  --name "$ACR_NAME" \
  --sku Basic \
  --admin-enabled true

echo "Logging into ACR..."
az acr login --name "$ACR_NAME"

echo "Tagging and pushing order-service image..."
docker tag order-service:latest "$ACR_NAME.azurecr.io/order-service:v1"
docker push "$ACR_NAME.azurecr.io/order-service:v1"

echo "Tagging and pushing shipping-service image..."
docker tag shipping-service:latest "$ACR_NAME.azurecr.io/shipping-service:v1"
docker push "$ACR_NAME.azurecr.io/shipping-service:v1"

echo "Tagging and pushing notification-service image..."
docker tag notification-service:latest "$ACR_NAME.azurecr.io/notification-service:v1"
docker push "$ACR_NAME.azurecr.io/notification-service:v1"
echo "Docker images pushed successfully to ACR."


# --- 2. Create Container App Environment ---
# This section creates the Azure Container Apps environment where the services will be deployed.

echo "Creating Log Analytics workspace: $LOG_ANALYTICS_WORKSPACE"
az monitor log-analytics workspace create \
  --resource-group "$RESOURCE_GROUP" \
  --workspace-name "$LOG_ANALYTICS_WORKSPACE"

echo "Fetching Log Analytics credentials..."
LOG_ANALYTICS_WORKSPACE_CLIENT_ID=$(az monitor log-analytics workspace show --query customerId -g "$RESOURCE_GROUP" -n "$LOG_ANALYTICS_WORKSPACE" --out tsv)
LOG_ANALYTICS_WORKSPACE_CLIENT_SECRET=$(az monitor log-analytics workspace get-shared-keys --query primarySharedKey -g "$RESOURCE_GROUP" -n "$LOG_ANALYTICS_WORKSPACE" --out tsv)

echo "Creating Container Apps environment: $CONTAINERAPPS_ENVIRONMENT"
az containerapp env create \
  --name "$CONTAINERAPPS_ENVIRONMENT" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --logs-workspace-id "$LOG_ANALYTICS_WORKSPACE_CLIENT_ID" \
  --logs-workspace-key "$LOG_ANALYTICS_WORKSPACE_CLIENT_SECRET"
echo "Container Apps environment created successfully."


# --- 3. Configure Dapr In-Memory Pub/Sub Component ---
# This section configures a Dapr pub/sub component for the Container Apps environment.
# For this example, we are using an in-memory pub/sub component, which is suitable for testing and development.
# For production, you would use a more robust component like Azure Service Bus or Redis.

echo "Creating Dapr in-memory pub/sub component YAML file..."
cat <<EOF > "$DAPR_COMPONENT_YAML_FILE"
componentType: pubsub.in-memory
version: v1
metadata: []
scopes:
- $ORDER_SERVICE_APP_NAME
- $SHIPPING_SERVICE_APP_NAME
- $NOTIFICATION_SERVICE_APP_NAME
EOF

echo "Setting Dapr pub/sub component in the environment..."
az containerapp env dapr-component set \
  --name "$CONTAINERAPPS_ENVIRONMENT" \
  --resource-group "$RESOURCE_GROUP" \
  --dapr-component-name "$DAPR_PUBSUB_COMPONENT_NAME" \
  --yaml "$DAPR_COMPONENT_YAML_FILE"
echo "Dapr component configured successfully."


# --- 4. Deploy Services to Azure Container Apps ---
# This section deploys the services as container apps to the created environment.

echo "Deploying Order Service..."
az containerapp create \
  --name "$ORDER_SERVICE_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$CONTAINERAPPS_ENVIRONMENT" \
  --image "$ACR_NAME.azurecr.io/order-service:v1" \
  --target-port 80 \
  --ingress 'external' \
  --registry-server "$ACR_NAME.azurecr.io" \
  --enable-dapr \
  --dapr-app-id "$ORDER_SERVICE_APP_NAME" \
  --dapr-app-port 80

echo "Deploying Shipping Service..."
az containerapp create \
  --name "$SHIPPING_SERVICE_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$CONTAINERAPPS_ENVIRONMENT" \
  --image "$ACR_NAME.azurecr.io/shipping-service:v1" \
  --target-port 80 \
  --ingress 'internal' \
  --registry-server "$ACR_NAME.azurecr.io" \
  --enable-dapr \
  --dapr-app-id "$SHIPPING_SERVICE_APP_NAME" \
  --dapr-app-port 80

echo "Deploying Notification Service..."
az containerapp create \
  --name "$NOTIFICATION_SERVICE_APP_NAME" \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$CONTAINERAPPS_ENVIRONMENT" \
  --image "$ACR_NAME.azurecr.io/notification-service:v1" \
  --target-port 80 \
  --ingress 'internal' \
  --registry-server "$ACR_NAME.azurecr.io" \
  --enable-dapr \
  --dapr-app-id "$NOTIFICATION_SERVICE_APP_NAME" \
  --dapr-app-port 80

echo "Deployment complete!"
