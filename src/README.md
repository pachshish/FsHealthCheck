# FsHealthCheck

**FsHealthCheck** is a synthetic filesystem health monitoring system for local disks and network shares (SMB / NFS).  
It performs real I/O operations to measure storage capacity, throughput, latency, and metadata performance, and exposes metrics for Prometheus + Grafana.

---

## Architecture Overview

```
                +----------------------+
                |  HealthCheck.Cli    |
                |  (Manual execution) |
                +----------+-----------+
                           |
                           v
                +----------------------+
                |  HealthCheck.Core   |
                |  (I/O test engine)  |
                +----------+-----------+
                           |
                           v
                +----------------------+
                | HealthCheck.Exporter |
                |  ASP.NET + Metrics   |
                +----------+-----------+
                           |
                           v
                +----------------------+
                | Prometheus           |
                | (Scrapes /metrics)   |
                +----------+-----------+
                           |
                           v
                +----------------------+
                | Grafana              |
                | Dashboards (Git)     |
                +----------------------+
```

---

## System Flow

### Manual Flow (CLI)

1. Load configuration (`HealthConfigRoot`)
2. For each share:
   - (Optional) Run stress load
   - Execute health checks
   - Print structured results

### Exporter Flow

1. Background service runs every `MetricsIntervalSeconds`
2. For each share:
   - Execute health checks
   - Update Prometheus metrics
3. Prometheus scrapes `/metrics`
4. Grafana visualizes time-series data

Manual trigger:
```
POST /api/healthcheck/run
```

---

## What Is Measured

### Capacity
- Total bytes
- Free bytes
- Free ratio

### Connection Latency
- Time to open + close a FileStream
- Represents SMB/NFS responsiveness

### Large File Throughput
- Sequential write (MB/s)
- Sequential read (MB/s)
- Windows supports unbuffered read

### Small Files Performance
- Create ops/sec
- Delete ops/sec

### Small I/O Latency
- 4KB write latency (ms)
- 4KB read latency (ms)

### Directory Listing
- Metadata traversal duration

### Error Metrics
- I/O error counter

---

## Project Structure

```
src/
 ├─ HealthCheck.Core
 ├─ HealthCheck.Cli
 └─ HealthCheck.Exporter

monitoring/
 ├─ docker-compose.yml
 ├─ prometheus.yml
 └─ grafana provisioning
```

---

## Running

### CLI
```
dotnet run --project ./src/HealthCheck.Cli -- path/to/appsettings.json
```

### Exporter
```
dotnet run --project ./src/HealthCheck.Exporter
```

Trigger manually:
```
curl -X POST http://localhost:5000/api/healthcheck/run
```

Metrics endpoint:
```
http://localhost:5000/metrics
```

---

## Monitoring Stack

From `/monitoring`:

```
docker compose up -d
```

- Prometheus: http://localhost:9090  
- Grafana: http://localhost:3000  

Dashboards are provisioned from Git.

---

## Design Principles

- Real synthetic I/O (not passive monitoring)
- Versioned dashboards
- Platform-aware implementation
- Controlled load generation
- Extensible architecture

---

## Operational Notes

- Each run creates a unique test file and deletes it after completion.
- Linux cannot fully disable OS page cache in portable user-space.
- Stress mode generates sustained load — use carefully in production.

---

© FsHealthCheck
