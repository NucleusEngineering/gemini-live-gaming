#!/bin/bash
# deploy.sh
# Deploys the Gemini Live Gamedev Assistant to Google Cloud Run

set -e

echo "Deploying to Google Cloud Run utilizing the default Compute Engine Service Account for Vertex AI."

gcloud run deploy gemini-live-assistant \
    --source . \
    --region us-central1 \
    --allow-unauthenticated \
    --min-instances=0 \
    --max-instances=5

echo "Deployment complete! Copy the Service URL above into your Unity Client."
