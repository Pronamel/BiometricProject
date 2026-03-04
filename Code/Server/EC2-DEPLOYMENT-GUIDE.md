# AWS EC2 Deployment Guide

## Pre-deployment Steps on AWS

### 1. Create EC2 Instance
- Launch an EC2 instance (Ubuntu 20.04 or newer recommended)
- Instance type: t3.micro or larger 
- Configure Security Group:
  - Allow HTTP (port 80) from 0.0.0.0/0
  - Allow HTTPS (port 443) from 0.0.0.0/0  
  - Allow SSH (port 22) from your IP

### 2. Connect to Your EC2 Instance
```bash
ssh -i your-key.pem ubuntu@your-ec2-public-ip
```

### 3. Upload Your Code
Option A - Using SCP:
```bash
scp -i your-key.pem -r ./Server ubuntu@your-ec2-public-ip:/home/ubuntu/
```

Option B - Using Git:
```bash
# On EC2 instance
git clone https://github.com/yourusername/yourrepository.git
cd yourrepository/Code/Server
```

### 4. Deploy Using Docker (Recommended)
```bash
# Make deploy script executable
chmod +x deploy-ec2.sh

# Run deployment script
./deploy-ec2.sh
```

OR manually:
```bash
# Build and run
docker build -t biometric-server .
docker run -d --name biometric-server -p 80:80 -p 443:443 -e ASPNETCORE_ENVIRONMENT=Production biometric-server
```

### 5. Using Docker Compose (Alternative)
```bash
docker-compose up -d
```

## Important Configuration Updates Needed

### Before Deployment:
1. **Update CORS origins** in Program.cs:
   - Replace `yourdomain.com` with your actual domain
   
2. **Update AllowedHosts** in appsettings.Production.json:
   - Replace `yourdomain.com` with your actual domain or EC2 public IP
   
3. **SSL Certificate** (for HTTPS):
   - For production, obtain proper SSL certificates
   - For testing, you can disable HTTPS temporarily

### DNS Configuration (Optional)
- Point your domain to the EC2 instance's Elastic IP
- Configure Route 53 or use your domain registrar's DNS

## Testing Your Deployment

After deployment, test these endpoints:
- `http://your-ec2-public-ip/weatherforecast/api/health`
- `http://your-ec2-public-ip/weatherforecast`

## Monitoring and Maintenance

### Check container logs:
```bash
docker logs biometric-server
```

### Check container status:
```bash
docker ps
```

### Restart the service:
```bash
docker restart biometric-server
```

## Security Considerations for Production

1. **Enable HTTPS**: Obtain proper SSL certificates (Let's Encrypt recommended)
2. **Firewall**: Use AWS Security Groups to restrict access
3. **Updates**: Regularly update your EC2 instance and container images
4. **Backup**: Implement backup strategies for your data
5. **Monitoring**: Set up CloudWatch or similar monitoring