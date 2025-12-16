# 🧭 Additional Setup

After installing and configuring your microservice, there are typically several infrastructure and deployment-related tasks that need to be handled. These are usually managed at the CI/CD level, configured as one-time manual setup operations, or a combination of both.

## 📋 Overview

The following items are commonly required for a production-ready microservice deployment but are typically handled outside of the application code itself:

1. **Database Deployments**
2. **NATS JetStream Streams & Consumer Creation**
3. **Secret Management Integration**

## 💾 Database Deployments

Before your microservice can run in a new environment, the database schema and any required initial data must be deployed. This includes:

- **Schema migrations**: Creating tables, indexes, constraints, and other database objects
- **Seed data**: Initial reference data, configuration values, or test data
- **Database credentials**: Ensuring proper database connection strings are configured

### Common Approaches

- **CI/CD pipelines**: Automated migrations run as part of deployment pipelines
- **Database migration tools**: Using tools like Entity Framework Migrations, FluentMigrator, or database-specific migration scripts
- **Infrastructure as Code**: Managing databases alongside application deployments using tools like Terraform, Pulumi, or cloud-native solutions

> 💡 **Tip**: Consider versioning your database schema alongside your application code and running migrations as part of your deployment process.

## 📡 NATS JetStream Streams & Consumer Creation

NATS JetStream provides persistent messaging capabilities. Your microservice may require specific streams and consumers to be configured before it can process messages correctly.

### What Needs to Be Set Up

- **Streams**: Define where messages are stored and retention policies
- **Consumers**: Configure how messages are delivered to your service instances
- **Subject mappings**: Ensure stream subjects match what your application expects

> Look into [Nats Controller for Kubernetes] (https://github.com/nats-io/nack) for GitOps route of setting this up in kubernetes cluster.

### Common Approaches

- **NATS CLI or scripts**: Using `nats` command-line tool or shell scripts to create streams and consumers
- **Infrastructure as Code**: Defining NATS resources in Terraform, Pulumi, or similar tools
- **Initialization containers**: Running setup scripts in Kubernetes init containers
- **Separate configuration service**: A dedicated service that manages NATS resources

> 💡 **Tip**: Consider storing your NATS configuration definitions in version control and treating them as infrastructure that should be reviewed and versioned alongside your application code.

## 🔐 Secret Management Integration

Your microservice likely needs access to secrets such as database credentials, API keys, or certificates. These should never be hardcoded in your application.

### Common Solutions

- **Doppler**: Environment-aware secret management platform
- **HashiCorp Vault**: Enterprise secret management solution
- **Cloud-native solutions**: AWS Secrets Manager, Azure Key Vault, Google Secret Manager
- **Kubernetes Secrets**: Native Kubernetes secret management (with encryption at rest)

### What to Configure

- **Secret store connection**: How your application authenticates to the secret management system
- **Secret rotation policies**: How often secrets are rotated and how applications handle rotation
- **Environment-specific secrets**: Different secrets for development, staging, and production
- **Access policies**: Ensuring services only have access to secrets they need

> 💡 **Tip**: Use environment variables or configuration files that are injected at runtime rather than committing secrets to version control. Many secret management tools provide CLI or SDK integration to fetch secrets at startup.

## 🎯 Implementation Notes

How these tasks are implemented varies significantly based on:

- **Your organization's infrastructure standards**: Whether you use Kubernetes, cloud-managed services, or on-premises solutions
- **CI/CD platform**: GitHub Actions, GitLab CI, Azure DevOps, Jenkins, etc.
- **Team preferences and tooling**: Different teams may have different approaches to managing infrastructure
- **Compliance requirements**: Some industries have specific requirements for database management and secret handling

Generally, these setup tasks are:

- **Infrastructure concerns**: Managed separately from application code
- **Environment-specific**: Different configurations for dev, staging, and production
- **One-time or automated**: Either set up once per environment or automated via CI/CD

## 📚 Related Documentation

- **[Application Configuration](./config.md)**: How to configure your microservice
- **[NATS Consumers](./consumer.md)**: How to define consumers in your application code
- **[Database Operations](./db.md)**: How to use databases within your microservice

---

[Back to documentation index](./README.md)
