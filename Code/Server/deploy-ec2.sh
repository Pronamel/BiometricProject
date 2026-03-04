#!/bin/bash

# EC2 Deployment Script for .NET Server
# Run this script on your EC2 instance

echo "Starting deployment process..."

# Update system packages
sudo apt update -y
sudo apt upgrade -y

# Install Docker if not already installed
if ! command -v docker &> /dev/null; then
    echo "Installing Docker..."
    sudo apt install -y docker.io
    sudo systemctl start docker
    sudo systemctl enable docker
    sudo usermod -aG docker $(whoami)
    echo "Docker installed. Please log out and back in for group changes to take effect."
fi

# Install Docker Compose if not already installed
if ! command -v docker-compose &> /dev/null; then
    echo "Installing Docker Compose..."
    sudo curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
    sudo chmod +x /usr/local/bin/docker-compose
fi

# Clone your repository (replace with your actual repo URL)
# git clone https://github.com/yourusername/yourrepository.git
# cd yourrepository/Code/Server

# Build and run the container
echo "Building Docker image..."
docker build -t biometric-server .

echo "Starting the container..."
docker run -d \
    --name biometric-server \
    -p 80:80 \
    -p 443:443 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    biometric-server

echo "Deployment complete!"
echo "Your server should now be accessible at: http://$(curl -s http://169.254.169.254/latest/meta-data/public-ipv4)"