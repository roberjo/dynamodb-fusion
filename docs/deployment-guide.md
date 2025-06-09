# Deployment Guide

Complete guide for deploying DynamoDB Fusion in various environments, from development to production.

## Table of Contents

1. [Deployment Overview](#deployment-overview)
2. [Docker Deployment](#docker-deployment)
3. [Kubernetes Deployment](#kubernetes-deployment)
4. [AWS ECS Deployment](#aws-ecs-deployment)
5. [Azure Container Apps](#azure-container-apps)
6. [Configuration Management](#configuration-management)
7. [Security Considerations](#security-considerations)
8. [Monitoring & Observability](#monitoring--observability)
9. [Troubleshooting](#troubleshooting)

## Deployment Overview

### Deployment Topologies

```
┌─────────────────────────────────────────────────────────────┐
│                    Load Balancer                            │
└─────────────────────┬───────────────────────────────────────┘
                      │
        ┌─────────────┼─────────────┐
        │             │             │
   ┌────▼───┐    ┌────▼───┐    ┌────▼───┐
   │Instance│    │Instance│    │Instance│
   │   1    │    │   2    │    │   3    │
   └────┬───┘    └────┬───┘    └────┬───┘
        │             │             │
        └─────────────┼─────────────┘
                      │
          ┌───────────▼────────────┐
          │  Distributed Cache     │
          │     (Redis)            │
          └───────────┬────────────┘
                      │
          ┌───────────▼────────────┐
          │      DynamoDB          │
          └────────────────────────┘
```

### Environment Types

| Environment | Purpose | Characteristics |
|-------------|---------|-----------------|
| **Development** | Local development | Single instance, local services |
| **Testing** | Automated testing | Isolated environment, test data |
| **Staging** | Pre-production validation | Production-like, safe for experiments |
| **Production** | Live system | High availability, monitoring, security |

## Docker Deployment

### 1. Dockerfile

Create `Dockerfile` in the project root:

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["src/DynamoDbFusion.Api/DynamoDbFusion.Api.csproj", "src/DynamoDbFusion.Api/"]
COPY ["src/DynamoDbFusion.Core/DynamoDbFusion.Core.csproj", "src/DynamoDbFusion.Core/"]
COPY ["src/DynamoDbFusion.Infrastructure/DynamoDbFusion.Infrastructure.csproj", "src/DynamoDbFusion.Infrastructure/"]
RUN dotnet restore "src/DynamoDbFusion.Api/DynamoDbFusion.Api.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/src/DynamoDbFusion.Api"
RUN dotnet build "DynamoDbFusion.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DynamoDbFusion.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Copy published application
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Start application
ENTRYPOINT ["dotnet", "DynamoDbFusion.Api.dll"]
```

### 2. Docker Compose

Create `docker-compose.prod.yml`:

```yaml
version: '3.8'

services:
  dynamodb-fusion:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - DynamoDbFusion__DynamoDb__Region=us-east-1
      - DynamoDbFusion__Cache__ConnectionString=redis:6379
    depends_on:
      - redis
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    restart: unless-stopped
    deploy:
      replicas: 3
      resources:
        limits:
          cpus: '1.0'
          memory: 512M
        reservations:
          cpus: '0.5'
          memory: 256M

  redis:
    image: redis:7-alpine
    command: redis-server --appendonly yes --requirepass ${REDIS_PASSWORD}
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    restart: unless-stopped
    deploy:
      resources:
        limits:
          cpus: '0.5'
          memory: 256M

  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
      - ./ssl:/etc/nginx/ssl:ro
    depends_on:
      - dynamodb-fusion
    restart: unless-stopped

volumes:
  redis_data:
```

### 3. Build and Deploy

```powershell
# Build the image
docker build -t dynamodb-fusion:latest .

# Tag for registry
docker tag dynamodb-fusion:latest your-registry/dynamodb-fusion:1.0.0

# Push to registry
docker push your-registry/dynamodb-fusion:1.0.0

# Deploy with compose
docker-compose -f docker-compose.prod.yml up -d
```

## Kubernetes Deployment

### 1. Namespace and ConfigMap

Create `k8s/namespace.yaml`:

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: dynamodb-fusion
  labels:
    name: dynamodb-fusion
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: dynamodb-fusion-config
  namespace: dynamodb-fusion
data:
  appsettings.json: |
    {
      "DynamoDbFusion": {
        "DynamoDb": {
          "Region": "us-east-1"
        },
        "Cache": {
          "Provider": "Redis",
          "EnableL1Cache": true,
          "EnableL2Cache": true
        },
        "Security": {
          "EnableAuthentication": true,
          "RateLimiting": {
            "RequestsPerMinute": 100
          }
        }
      }
    }
```

### 2. Deployment Manifest

Create `k8s/deployment.yaml`:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dynamodb-fusion
  namespace: dynamodb-fusion
  labels:
    app: dynamodb-fusion
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 1
  selector:
    matchLabels:
      app: dynamodb-fusion
  template:
    metadata:
      labels:
        app: dynamodb-fusion
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "8080"
        prometheus.io/path: "/metrics"
    spec:
      serviceAccountName: dynamodb-fusion-sa
      containers:
      - name: dynamodb-fusion
        image: your-registry/dynamodb-fusion:1.0.0
        imagePullPolicy: IfNotPresent
        ports:
        - containerPort: 8080
          name: http
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: DynamoDbFusion__Cache__ConnectionString
          valueFrom:
            secretKeyRef:
              name: redis-secret
              key: connection-string
        - name: AWS_REGION
          value: "us-east-1"
        volumeMounts:
        - name: config-volume
          mountPath: /app/appsettings.json
          subPath: appsettings.json
        - name: aws-credentials
          mountPath: /root/.aws
          readOnly: true
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 30
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
          timeoutSeconds: 3
          failureThreshold: 3
      volumes:
      - name: config-volume
        configMap:
          name: dynamodb-fusion-config
      - name: aws-credentials
        secret:
          secretName: aws-credentials
      imagePullSecrets:
      - name: registry-secret
```

### 3. Service and Ingress

Create `k8s/service.yaml`:

```yaml
apiVersion: v1
kind: Service
metadata:
  name: dynamodb-fusion-service
  namespace: dynamodb-fusion
  labels:
    app: dynamodb-fusion
spec:
  type: ClusterIP
  ports:
  - port: 80
    targetPort: 8080
    protocol: TCP
    name: http
  selector:
    app: dynamodb-fusion
---
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: dynamodb-fusion-ingress
  namespace: dynamodb-fusion
  annotations:
    kubernetes.io/ingress.class: "nginx"
    cert-manager.io/cluster-issuer: "letsencrypt-prod"
    nginx.ingress.kubernetes.io/rate-limit: "100"
    nginx.ingress.kubernetes.io/rate-limit-window: "1m"
spec:
  tls:
  - hosts:
    - api.yourdomain.com
    secretName: dynamodb-fusion-tls
  rules:
  - host: api.yourdomain.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: dynamodb-fusion-service
            port:
              number: 80
```

### 4. RBAC Configuration

Create `k8s/rbac.yaml`:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: dynamodb-fusion-sa
  namespace: dynamodb-fusion
  annotations:
    eks.amazonaws.com/role-arn: arn:aws:iam::123456789012:role/DynamoDbFusionRole
---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  namespace: dynamodb-fusion
  name: dynamodb-fusion-role
rules:
- apiGroups: [""]
  resources: ["configmaps", "secrets"]
  verbs: ["get", "list"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: dynamodb-fusion-binding
  namespace: dynamodb-fusion
subjects:
- kind: ServiceAccount
  name: dynamodb-fusion-sa
  namespace: dynamodb-fusion
roleRef:
  kind: Role
  name: dynamodb-fusion-role
  apiGroup: rbac.authorization.k8s.io
```

### 5. Deploy to Kubernetes

```powershell
# Apply manifests
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/rbac.yaml
kubectl apply -f k8s/secrets.yaml
kubectl apply -f k8s/deployment.yaml
kubectl apply -f k8s/service.yaml

# Check deployment status
kubectl get pods -n dynamodb-fusion
kubectl logs -f deployment/dynamodb-fusion -n dynamodb-fusion

# Scale deployment
kubectl scale deployment dynamodb-fusion --replicas=5 -n dynamodb-fusion
```

## AWS ECS Deployment

### 1. Task Definition

Create `ecs-task-definition.json`:

```json
{
  "family": "dynamodb-fusion",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "executionRoleArn": "arn:aws:iam::123456789012:role/ecsTaskExecutionRole",
  "taskRoleArn": "arn:aws:iam::123456789012:role/dynamodb-fusion-task-role",
  "containerDefinitions": [
    {
      "name": "dynamodb-fusion",
      "image": "123456789012.dkr.ecr.us-east-1.amazonaws.com/dynamodb-fusion:latest",
      "portMappings": [
        {
          "containerPort": 8080,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        },
        {
          "name": "DynamoDbFusion__DynamoDb__Region",
          "value": "us-east-1"
        }
      ],
      "secrets": [
        {
          "name": "DynamoDbFusion__Cache__ConnectionString",
          "valueFrom": "arn:aws:ssm:us-east-1:123456789012:parameter/dynamodb-fusion/redis-connection"
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/dynamodb-fusion",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      },
      "healthCheck": {
        "command": [
          "CMD-SHELL",
          "curl -f http://localhost:8080/health || exit 1"
        ],
        "interval": 30,
        "timeout": 5,
        "retries": 3,
        "startPeriod": 60
      }
    }
  ]
}
```

### 2. ECS Service Definition

Create `ecs-service.json`:

```json
{
  "serviceName": "dynamodb-fusion",
  "cluster": "production-cluster",
  "taskDefinition": "dynamodb-fusion",
  "desiredCount": 3,
  "launchType": "FARGATE",
  "networkConfiguration": {
    "awsvpcConfiguration": {
      "subnets": [
        "subnet-12345678",
        "subnet-87654321"
      ],
      "securityGroups": [
        "sg-12345678"
      ],
      "assignPublicIp": "DISABLED"
    }
  },
  "loadBalancers": [
    {
      "targetGroupArn": "arn:aws:elasticloadbalancing:us-east-1:123456789012:targetgroup/dynamodb-fusion/1234567890123456",
      "containerName": "dynamodb-fusion",
      "containerPort": 8080
    }
  ],
  "deploymentConfiguration": {
    "minimumHealthyPercent": 50,
    "maximumPercent": 200,
    "deploymentCircuitBreaker": {
      "enable": true,
      "rollback": true
    }
  },
  "enableExecuteCommand": true
}
```

### 3. CloudFormation Template

Create `cloudformation-template.yaml`:

```yaml
AWSTemplateFormatVersion: '2010-09-09'
Description: 'DynamoDB Fusion ECS Deployment'

Parameters:
  ImageUri:
    Type: String
    Description: ECR image URI
  
  VpcId:
    Type: AWS::EC2::VPC::Id
    Description: VPC ID
  
  SubnetIds:
    Type: List<AWS::EC2::Subnet::Id>
    Description: Subnet IDs

Resources:
  ECSCluster:
    Type: AWS::ECS::Cluster
    Properties:
      ClusterName: dynamodb-fusion-cluster
      CapacityProviders:
        - FARGATE
        - FARGATE_SPOT

  TaskDefinition:
    Type: AWS::ECS::TaskDefinition
    Properties:
      Family: dynamodb-fusion
      Cpu: 512
      Memory: 1024
      NetworkMode: awsvpc
      RequiresCompatibilities:
        - FARGATE
      ExecutionRoleArn: !Ref TaskExecutionRole
      TaskRoleArn: !Ref TaskRole
      ContainerDefinitions:
        - Name: dynamodb-fusion
          Image: !Ref ImageUri
          PortMappings:
            - ContainerPort: 8080
          Environment:
            - Name: ASPNETCORE_ENVIRONMENT
              Value: Production
          LogConfiguration:
            LogDriver: awslogs
            Options:
              awslogs-group: !Ref LogGroup
              awslogs-region: !Ref AWS::Region
              awslogs-stream-prefix: ecs

  Service:
    Type: AWS::ECS::Service
    Properties:
      ServiceName: dynamodb-fusion
      Cluster: !Ref ECSCluster
      TaskDefinition: !Ref TaskDefinition
      DesiredCount: 3
      LaunchType: FARGATE
      NetworkConfiguration:
        AwsvpcConfiguration:
          Subnets: !Ref SubnetIds
          SecurityGroups:
            - !Ref SecurityGroup
          AssignPublicIp: DISABLED

  SecurityGroup:
    Type: AWS::EC2::SecurityGroup
    Properties:
      GroupDescription: Security group for DynamoDB Fusion
      VpcId: !Ref VpcId
      SecurityGroupIngress:
        - IpProtocol: tcp
          FromPort: 8080
          ToPort: 8080
          SourceSecurityGroupId: !Ref LoadBalancerSecurityGroup

  LoadBalancerSecurityGroup:
    Type: AWS::EC2::SecurityGroup
    Properties:
      GroupDescription: Security group for Load Balancer
      VpcId: !Ref VpcId
      SecurityGroupIngress:
        - IpProtocol: tcp
          FromPort: 80
          ToPort: 80
          CidrIp: 0.0.0.0/0
        - IpProtocol: tcp
          FromPort: 443
          ToPort: 443
          CidrIp: 0.0.0.0/0

  TaskExecutionRole:
    Type: AWS::IAM::Role
    Properties:
      AssumeRolePolicyDocument:
        Version: '2012-10-17'
        Statement:
          - Effect: Allow
            Principal:
              Service: ecs-tasks.amazonaws.com
            Action: sts:AssumeRole
      ManagedPolicyArns:
        - arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy

  TaskRole:
    Type: AWS::IAM::Role
    Properties:
      AssumeRolePolicyDocument:
        Version: '2012-10-17'
        Statement:
          - Effect: Allow
            Principal:
              Service: ecs-tasks.amazonaws.com
            Action: sts:AssumeRole
      Policies:
        - PolicyName: DynamoDBAccess
          PolicyDocument:
            Version: '2012-10-17'
            Statement:
              - Effect: Allow
                Action:
                  - dynamodb:Query
                  - dynamodb:Scan
                  - dynamodb:GetItem
                  - dynamodb:DescribeTable
                Resource: '*'

  LogGroup:
    Type: AWS::Logs::LogGroup
    Properties:
      LogGroupName: /ecs/dynamodb-fusion
      RetentionInDays: 30
```

### 4. Deploy to ECS

```powershell
# Deploy CloudFormation stack
aws cloudformation deploy `
  --template-file cloudformation-template.yaml `
  --stack-name dynamodb-fusion `
  --parameter-overrides `
    ImageUri=123456789012.dkr.ecr.us-east-1.amazonaws.com/dynamodb-fusion:latest `
    VpcId=vpc-12345678 `
    SubnetIds=subnet-12345678,subnet-87654321 `
  --capabilities CAPABILITY_IAM

# Update service
aws ecs update-service `
  --cluster production-cluster `
  --service dynamodb-fusion `
  --task-definition dynamodb-fusion:2
```

## Azure Container Apps

### 1. Container App Definition

Create `containerapp.yaml`:

```yaml
apiVersion: apps/v1alpha1
kind: ContainerApp
metadata:
  name: dynamodb-fusion
spec:
  environmentId: /subscriptions/{subscription-id}/resourceGroups/{rg}/providers/Microsoft.App/managedEnvironments/{env-name}
  template:
    containers:
    - name: dynamodb-fusion
      image: youracr.azurecr.io/dynamodb-fusion:latest
      resources:
        cpu: 0.5
        memory: 1Gi
      env:
      - name: ASPNETCORE_ENVIRONMENT
        value: Production
      - name: DynamoDbFusion__DynamoDb__Region
        value: us-east-1
      - name: DynamoDbFusion__Cache__ConnectionString
        secretRef: redis-connection-string
    scale:
      minReplicas: 1
      maxReplicas: 10
      rules:
      - name: http-scaling
        http:
          metadata:
            concurrentRequests: '100'
  ingress:
    external: true
    targetPort: 8080
    traffic:
    - weight: 100
      latestRevision: true
```

### 2. Deploy to Azure

```powershell
# Create resource group
az group create --name dynamodb-fusion-rg --location eastus

# Create container app environment
az containerapp env create `
  --name dynamodb-fusion-env `
  --resource-group dynamodb-fusion-rg `
  --location eastus

# Deploy container app
az containerapp create `
  --name dynamodb-fusion `
  --resource-group dynamodb-fusion-rg `
  --environment dynamodb-fusion-env `
  --image youracr.azurecr.io/dynamodb-fusion:latest `
  --target-port 8080 `
  --ingress external `
  --min-replicas 1 `
  --max-replicas 10
```

## Configuration Management

### 1. Environment Variables

| Environment Variable | Description | Example |
|---------------------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core environment | `Production` |
| `DynamoDbFusion__DynamoDb__Region` | AWS region | `us-east-1` |
| `DynamoDbFusion__Cache__ConnectionString` | Redis connection string | `localhost:6379` |
| `AWS_ACCESS_KEY_ID` | AWS access key | `AKIAIOSFODNN7EXAMPLE` |
| `AWS_SECRET_ACCESS_KEY` | AWS secret key | `wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY` |

### 2. Secrets Management

**Kubernetes Secrets:**
```powershell
# Create secret from literal
kubectl create secret generic redis-secret `
  --from-literal=connection-string="redis://user:password@redis:6379" `
  -n dynamodb-fusion

# Create secret from file
kubectl create secret generic aws-credentials `
  --from-file=credentials=/path/to/aws/credentials `
  -n dynamodb-fusion
```

**AWS Systems Manager:**
```powershell
# Store parameter
aws ssm put-parameter `
  --name "/dynamodb-fusion/redis-connection" `
  --value "redis://user:password@redis:6379" `
  --type "SecureString"

# Store parameter with KMS key
aws ssm put-parameter `
  --name "/dynamodb-fusion/api-key" `
  --value "secret-api-key" `
  --type "SecureString" `
  --key-id "alias/parameter-store-key"
```

### 3. Configuration Validation

```csharp
public class ConfigurationValidator : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationValidator> _logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateRequiredSettings();
        await ValidateExternalDependencies();
    }

    private void ValidateRequiredSettings()
    {
        var requiredSettings = new[]
        {
            "DynamoDbFusion:DynamoDb:Region",
            "DynamoDbFusion:Cache:ConnectionString"
        };

        foreach (var setting in requiredSettings)
        {
            if (string.IsNullOrEmpty(_configuration[setting]))
            {
                throw new InvalidOperationException($"Required setting '{setting}' is missing");
            }
        }
    }

    private async Task ValidateExternalDependencies()
    {
        // Validate DynamoDB connectivity
        // Validate Redis connectivity
        // Validate any other external dependencies
    }
}
```

## Security Considerations

### 1. IAM Policies

**DynamoDB Access Policy:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "dynamodb:Query",
        "dynamodb:Scan",
        "dynamodb:GetItem",
        "dynamodb:DescribeTable",
        "dynamodb:ListTables"
      ],
      "Resource": [
        "arn:aws:dynamodb:us-east-1:123456789012:table/Users",
        "arn:aws:dynamodb:us-east-1:123456789012:table/Users/index/*"
      ]
    }
  ]
}
```

### 2. Network Security

**Security Groups:**
```
Inbound Rules:
- Port 8080: Source = Load Balancer Security Group
- Port 443: Source = 0.0.0.0/0 (HTTPS)

Outbound Rules:
- Port 443: Destination = 0.0.0.0/0 (DynamoDB, SSM)
- Port 6379: Destination = Redis Security Group
```

### 3. TLS Configuration

```csharp
// In Program.cs
app.UseHttpsRedirection();
app.UseHsts();

// Configure Kestrel for production
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1_000_000; // 1MB
});
```

## Monitoring & Observability

### 1. Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<DynamoDbHealthCheck>("dynamodb")
    .AddCheck<RedisHealthCheck>("redis")
    .AddCheck<MemoryHealthCheck>("memory");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### 2. Application Insights

```csharp
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights");
});
```

### 3. Prometheus Metrics

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(builder =>
    {
        builder.AddPrometheusExporter();
        builder.AddMeter("DynamoDbFusion");
    });

app.MapPrometheusScrapingEndpoint();
```

## Troubleshooting

### Common Issues

**1. Container Won't Start**
```powershell
# Check logs
docker logs container-id
kubectl logs pod-name -n namespace

# Check events
kubectl describe pod pod-name -n namespace
```

**2. High Memory Usage**
```powershell
# Monitor memory
kubectl top pods -n dynamodb-fusion

# Check for memory leaks
dotnet-dump collect -p process-id
```

**3. DynamoDB Connection Issues**
```powershell
# Test connectivity
aws dynamodb list-tables --region us-east-1

# Check IAM permissions
aws sts get-caller-identity
```

**4. Redis Connection Issues**
```powershell
# Test Redis connectivity
redis-cli -h redis-host -p 6379 ping

# Check Redis logs
kubectl logs redis-pod -n namespace
```

This deployment guide provides comprehensive coverage for deploying DynamoDB Fusion across various platforms and environments. Adapt the configurations based on your specific requirements and infrastructure setup. 