# Database Administrator Guide

This guide is for administrators who want to **set up and manage a MongoDB database** for AI-CAD users.

---

## 🎯 **Overview**

As a database administrator, you'll:
1. Create and configure a MongoDB cluster
2. Set up appropriate user permissions
3. Share connection details with users
4. Monitor and maintain the database
5. Manage data growth and performance

---

## 🚀 **Quick Start: Free MongoDB Atlas Setup**

### **1. Create MongoDB Atlas Account**

```bash
# Visit MongoDB Atlas
https://www.mongodb.com/cloud/atlas/register

# Sign up (FREE - no credit card required)
- Business Email
- First Name / Last Name
- Password (strong recommended)
```

### **2. Create a Free Cluster**

1. After login, click **"Build a Database"**
2. Choose **Shared (FREE)** tier
3. Select provider: **AWS, Google Cloud, or Azure**
4. Choose region: **Closest to your users**
5. Cluster Name: `AI-CAD-Cluster` (or your choice)
6. Click **"Create"**
7. Wait 3-5 minutes for provisioning

### **3. Create Database Users**

#### **Read/Write User (for end users)**

```javascript
// In Atlas Dashboard → Database Access → Add New Database User

Username: aicad_user
Authentication Method: Password
Password: <generate strong password>

Database User Privileges:
- Built-in Role: "Read and write to any database"

Specific Privileges (optional, more restrictive):
- Database: AI_CAD_Production
  Collection: runs, steps, run_feedback, good_feedback
  Privilege: Read + Write
```

#### **Read-Only User (for analysis/reporting)**

```javascript
Username: aicad_readonly
Password: <different strong password>

Database User Privileges:
- Built-in Role: "Only read any database"
```

#### **Admin User (for you)**

```javascript
Username: admin_user
Password: <very strong password + 2FA>

Database User Privileges:
- Built-in Role: "Atlas admin"
```

### **4. Configure Network Access**

```bash
# Go to: Network Access → Add IP Address

Option A: Allow from Anywhere (easiest for distributed teams)
IP Address: 0.0.0.0/0
Comment: "All AI-CAD users"

Option B: Specific IP ranges (more secure)
IP Address: 192.168.1.0/24
Comment: "Office network"

# Add multiple entries for different locations
```

### **5. Get Connection String**

```bash
# Go to: Database → Connect → Connect your application

Driver: C# / .NET
Version: 2.13 or later

Connection String Format:
mongodb+srv://<username>:<password>@ai-cad-cluster.xxxxx.mongodb.net/?retryWrites=true&w=majority

Example:
mongodb+srv://aicad_user:MySecurePass123!@ai-cad-cluster.abc123.mongodb.net/?retryWrites=true&w=majority
```

---

## 📊 **Database Schema**

AI-CAD uses the following collections:

### **Collection: `runs`**
Stores complete run history with prompts and generated plans.

```javascript
{
  _id: ObjectId("..."),
  run_key: "RUN_20260102_143052_abc123",     // Unique run identifier
  ts: ISODate("2026-01-02T14:30:52.123Z"),   // Timestamp
  prompt: "Create a 100mm cube",              // User's request
  model: "gemini-1.5-flash",                  // AI model used
  plan: "{\"steps\":[...]}",                  // Generated JSON plan
  success: true,                              // Whether execution succeeded
  llm_ms: 1234,                               // AI response time (ms)
  total_ms: 5678,                             // Total execution time (ms)
  error: ""                                   // Error message if failed
}

// Recommended Indexes:
db.runs.createIndex({ "ts": -1 })                    // Sort by time
db.runs.createIndex({ "success": 1, "ts": -1 })      // Few-shot queries
db.runs.createIndex({ "run_key": 1 }, { unique: true }) // Unique runs
```

### **Collection: `steps`**
Individual CAD operations for each run.

```javascript
{
  _id: ObjectId("..."),
  run_key: "RUN_20260102_143052_abc123",     // Links to parent run
  step_index: 0,                              // Order in sequence
  op: "sketch_rectangle",                     // Operation type
  params_json: "{\"width\":100,\"height\":100}", // Parameters
  success: true,                              // Step succeeded
  error: ""                                   // Error if failed
}

// Recommended Indexes:
db.steps.createIndex({ "run_key": 1, "step_index": 1 })
```

### **Collection: `run_feedback`**
User ratings and comments.

```javascript
{
  _id: ObjectId("..."),
  run_key: "RUN_20260102_143052_abc123",
  ts: ISODate("2026-01-02T14:31:00.000Z"),
  thumb: "up",                                // "up" or "down"
  comment: "Perfect result!"                  // Optional user comment
}

// Recommended Indexes:
db.run_feedback.createIndex({ "run_key": 1 })
db.run_feedback.createIndex({ "ts": -1 })
```

### **Collection: `good_feedback`**
High-quality examples marked by users.

```javascript
{
  _id: ObjectId("..."),
  ts: ISODate("2026-01-02T14:31:00.000Z"),
  runId: "RUN_20260102_143052_abc123",
  prompt: "Create a 100mm cube",
  model: "gemini-1.5-flash",
  plan: "{\"steps\":[...]}",                  // Complete successful plan
  comment: "Great example of simple box"
}

// Recommended Indexes:
db.good_feedback.createIndex({ "ts": -1 })
db.good_feedback.createIndex({ "prompt": "text" })   // Text search
```

### **Collection: `users`** (optional)
User account information if using Google Sign-In.

```javascript
{
  _id: ObjectId("..."),
  email: "user@example.com",
  displayName: "John Doe",
  lastLogin: ISODate("2026-01-02T14:30:00.000Z"),
  createdAt: ISODate("2026-01-01T10:00:00.000Z")
}

// Recommended Indexes:
db.users.createIndex({ "email": 1 }, { unique: true })
```

---

## 🔧 **Database Maintenance**

### **Create Indexes (Important for Performance)**

```javascript
// Connect to your database with mongosh or MongoDB Compass

use AI_CAD_Production

// Create all recommended indexes
db.runs.createIndex({ "ts": -1 })
db.runs.createIndex({ "success": 1, "ts": -1 })
db.runs.createIndex({ "run_key": 1 }, { unique: true })
db.steps.createIndex({ "run_key": 1, "step_index": 1 })
db.run_feedback.createIndex({ "run_key": 1 })
db.run_feedback.createIndex({ "ts": -1 })
db.good_feedback.createIndex({ "ts": -1 })
db.good_feedback.createIndex({ "prompt": "text" })
db.users.createIndex({ "email": 1 }, { unique: true })

// Verify indexes created
db.runs.getIndexes()
```

### **Monitor Database Size**

```javascript
// Check database statistics
db.stats()

// Check collection sizes
db.runs.stats()
db.steps.stats()

// Count documents
db.runs.countDocuments()
db.good_feedback.countDocuments()
```

### **Data Retention Policy**

```javascript
// Example: Delete runs older than 6 months
db.runs.deleteMany({
  ts: { $lt: new Date(Date.now() - 180 * 24 * 60 * 60 * 1000) }
})

// Or create TTL index for automatic deletion
db.runs.createIndex(
  { "ts": 1 },
  { expireAfterSeconds: 15552000 }  // 180 days
)
```

### **Backup Strategy**

```bash
# MongoDB Atlas provides automated backups (FREE tier: continuous backups)

# Manual backup using mongodump:
mongodump --uri="mongodb+srv://admin_user:password@cluster.mongodb.net/AI_CAD_Production" --out=backup_20260102

# Restore from backup:
mongorestore --uri="mongodb+srv://admin_user:password@cluster.mongodb.net/AI_CAD_Production" backup_20260102/AI_CAD_Production
```

---

## 📈 **Performance Optimization**

### **Query Performance Monitoring**

In MongoDB Atlas:
1. Go to **Performance** tab
2. Review **Slow Queries** (> 100ms)
3. Add missing indexes as needed

### **Connection Pooling**

AI-CAD uses default connection pooling (100 connections max).

To adjust for large teams:
```
mongodb+srv://user:pass@cluster.net/?maxPoolSize=200
```

### **Scaling**

#### **Vertical Scaling (Upgrade Cluster)**
- FREE (M0): 512MB, Shared CPU
- M2: 2GB, Shared CPU - $9/month
- M10: 10GB, Dedicated - $57/month
- M20+: Production-grade

#### **Horizontal Scaling (Sharding)**
Available on M30+ clusters for very large datasets.

---

## 🔐 **Security Checklist**

- [ ] ✅ Strong passwords (16+ characters, mixed case, numbers, symbols)
- [ ] ✅ Enable 2FA on MongoDB Atlas admin account
- [ ] ✅ Use IP whitelisting when possible
- [ ] ✅ Separate credentials for read-only vs read-write access
- [ ] ✅ Rotate passwords every 90 days
- [ ] ✅ Monitor access logs in Atlas dashboard
- [ ] ✅ Enable encryption at rest (FREE tier includes this)
- [ ] ✅ Use TLS/SSL for connections (enabled by default with mongodb+srv://)
- [ ] ✅ Review and revoke unused database users
- [ ] ✅ Set up alerts for suspicious activity

---

## 👥 **User Management**

### **Add New User**

```javascript
// In Atlas: Database Access → Add New Database User
Username: new_user
Password: <auto-generate recommended>
Role: readWriteAnyDatabase
```

### **Revoke User Access**

```javascript
// In Atlas: Database Access → Find user → Delete
// Or temporarily disable without deleting
```

### **Audit User Activity**

```javascript
// MongoDB Atlas provides access logs
// Go to: Activity Feed

// Query recent operations
db.system.profile.find().sort({ts:-1}).limit(10)
```

---

## 📞 **Sharing Connection Details**

### **Template Email for Users**

```
Subject: AI-CAD Shared Database Setup

Hi Team,

To enable collaborative learning in AI-CAD, please configure these database settings:

CONNECTION DETAILS:
-------------------
Server Address: ai-cad-cluster.abc123.mongodb.net
Port: 27017
Authentication: Username & Password
Username: aicad_user
Password: [See password manager / separate secure channel]
Database Name: AI_CAD_Production

SETUP INSTRUCTIONS:
-------------------
1. Open SolidWorks → AI-CAD Settings
2. Click "Database" tab
3. Enter the details above
4. Click "Test Connection" (should show green ✓)
5. Click "Save Changes"
6. Restart SolidWorks

DOCUMENTATION:
--------------
Full setup guide: [Link to SHARED_DATABASE_SETUP.md]

Questions? Reply to this email or check our wiki.

Thanks!
```

### **Connection String Format**

Provide users with:
```
mongodb+srv://aicad_user:PASSWORD@ai-cad-cluster.abc123.mongodb.net/?retryWrites=true&w=majority
```

**⚠️ Security Note:** Share passwords via secure channels (password manager, encrypted email, Slack DM, etc.)

---

## 🛠️ **Troubleshooting**

### **Problem: Users can't connect**

```bash
# Check:
1. ✅ User credentials are correct
2. ✅ IP whitelist includes user's IP (or 0.0.0.0/0)
3. ✅ Database user has proper permissions
4. ✅ Cluster is running (check Atlas dashboard)
5. ✅ Firewall allows outbound port 27017

# Test connection:
mongosh "mongodb+srv://aicad_user:password@cluster.mongodb.net/AI_CAD_Production"
```

### **Problem: Slow queries**

```javascript
// Enable profiling
db.setProfilingLevel(2)  // Log all queries

// Review slow queries
db.system.profile.find({ millis: { $gt: 100 } }).sort({ ts: -1 })

// Add missing indexes
```

### **Problem: Database full (FREE tier limit)**

```javascript
// Check current size
db.stats().dataSize / (1024 * 1024)  // Size in MB

// Clean up old data
db.runs.deleteMany({ ts: { $lt: new Date('2025-01-01') } })

// Or upgrade to paid tier
```

---

## 📊 **Monitoring & Alerts**

### **Set Up Alerts in MongoDB Atlas**

1. Go to **Alerts** → **Add New Alert**
2. Recommended alerts:
   - **Disk Usage** > 80%
   - **Connections** > 90% of max
   - **Query Execution Time** > 1000ms
   - **Replication Lag** > 10 seconds

### **Monthly Health Check**

- [ ] Review database size growth
- [ ] Check slow queries and add indexes
- [ ] Verify backups are working
- [ ] Review user access (remove inactive users)
- [ ] Check for errors in application logs
- [ ] Update connection passwords (quarterly)

---

## 🎓 **Advanced: MongoDB Compass GUI**

For easier database management, use [MongoDB Compass](https://www.mongodb.com/products/compass):

```bash
# Download: https://www.mongodb.com/try/download/compass

# Connect using connection string:
mongodb+srv://admin_user:password@cluster.mongodb.net/AI_CAD_Production

# Features:
- Visual query builder
- Index management
- Schema analysis
- Data visualization
- Import/Export data
```

---

## 📚 **Resources**

- [MongoDB Atlas Documentation](https://docs.atlas.mongodb.com/)
- [MongoDB University (FREE courses)](https://university.mongodb.com/)
- [MongoDB C# Driver Documentation](https://mongodb.github.io/mongo-csharp-driver/)
- [Security Best Practices](https://www.mongodb.com/docs/manual/security/)

---

## 🎉 **Success!**

You now have a fully configured MongoDB database for AI-CAD users. Your team can now:

✅ Share successful CAD generation examples  
✅ Benefit from collective few-shot learning  
✅ Improve AI accuracy through community feedback  
✅ Track usage and performance metrics  

**Questions?** Open an issue on GitHub or contact the development team.
