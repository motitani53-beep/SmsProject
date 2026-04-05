# SMS Gateway System - Code Architecture Report

## Overview
This is an SMS Gateway system built with .NET 9.0, consisting of three main components that work together to handle SMS campaign creation, message processing, and delivery status tracking.

---

## 1. SmscServer Project

### Purpose
A mock SMSC (Short Message Service Center) server that simulates a real SMS provider's server for testing and development purposes.

### Components

#### **Program.cs**
**Job:** Application entry point and host configuration
**Responsibilities:**
- Builds configuration from `appsettings.json` and command-line arguments
- Configures dependency injection container
- Registers `SmscServerService` as a hosted background service
- Sets up console logging
- Starts and runs the host application
- Logs server startup information (port, timeout settings)

#### **SmscServerService.cs**
**Job:** Background service that runs the SMSC mock server
**Responsibilities:**
- Creates and manages an SMPP server instance on a configurable port (default: 2775)
- Handles client connections (Transmitter, Receiver, Transceiver modes)
- Processes incoming `SubmitSm` (Submit Short Message) requests from clients
- Generates unique message IDs (16-character GUID)
- Sends immediate `SubmitSmResp` (response) to the submitting client
- Simulates delivery receipt (DLR) by creating and sending `DeliverSm` PDU after 1 second delay
- Routes DLR to connected Receiver/Transceiver clients
- Manages server lifecycle (start/stop)
- Logs all connection and message events

**Key Features:**
- Accepts all client bind requests without authentication
- Creates delivery receipts with status "DELIVRD" (delivered)
- Handles multiple client connections simultaneously

---

## 2. WebApplication1 Project (Main Web API)

### Purpose
RESTful API service that handles SMS campaign management, message processing, and status tracking.

### Components

#### **Program.cs**
**Job:** Application startup and service configuration
**Responsibilities:**
- Configures Serilog for file and console logging
- Sets up dependency injection for all services
- Configures PostgreSQL database connection using Entity Framework Core
- Registers services: `SenderPhoneNumberService`, `MessageProcessingService`, `RabbitMqService`, `RabbitMqTopicSetupService`
- Configures CORS (allows all origins, methods, headers)
- Ensures database migrations are applied on startup
- Ensures RabbitMQ topics/exchanges/queues are created on startup
- Configures HTTP request pipeline (CORS, HTTPS redirection, authorization, controllers)
- Maps OpenAPI endpoint in development mode

#### **CampaignController.cs**
**Job:** HTTP API endpoints for campaign management
**Responsibilities:**
- **POST `/api/campaign`** - Creates a new SMS campaign
  - Validates request model
  - Validates scheduling configuration
  - Creates `Campaign` entity in database
  - Creates `DeliveryDetails` records for each recipient
  - Handles dynamic field replacement via recipient `custom_fields` stored as JSONB (`AdditionalData`)
  - Publishes campaign messages to RabbitMQ via `MessageProcessingService`
  - Returns campaign ID and status
- **GET `/api/campaign/{id}`** - Retrieves a specific campaign with delivery details
- **GET `/api/campaign`** - Retrieves all campaigns ordered by creation date

#### **StatusController.cs**
**Job:** API endpoints for system status monitoring
**Responsibilities:**
- **GET `/api/status/smsc`** - Returns SMSC connection status
  - Queries `SmscStatus` table from database
  - Returns connection status, last enquire link time, last error, and update timestamp
  - Returns default values if no status record exists

#### **RabbitMqService.cs**
**Job:** RabbitMQ message broker client service
**Responsibilities:**
- Manages RabbitMQ connection and channel lifecycle
- Implements connection pooling with thread-safe locking
- Provides `Publish()` method for raw byte array messages
- Provides `PublishJson<T>()` method for JSON-serialized messages
- Ensures connection is established before publishing
- Sets message properties (persistent, content-type)
- Handles connection disposal and cleanup
- Implements `IDisposable` pattern

#### **IRabbitMqService.cs**
**Job:** Interface definition for RabbitMQ service
**Responsibilities:**
- Defines contract for RabbitMQ operations
- Documents method purposes with XML comments
- Enables dependency injection and testing

#### **MessageProcessingService.cs**
**Job:** Business logic for processing and publishing SMS messages
**Responsibilities:**
- Replaces message template placeholders (`{key}` per dictionary key) with values from recipient custom fields / JSONB
- Publishes campaign messages to RabbitMQ after database persistence
- Distributes messages across multiple RabbitMQ topics using modulo operation (`campaignId % numberOfTopics`)
- Ensures message ordering per campaign by using same routing key for all messages in a campaign
- Flattens JSON object in JSONB `AdditionalData` to string values for placeholder replacement when publishing
- Creates `SmsMessageDto` objects with delivery ID, campaign ID, phone number, message text, and actual sender
- Uses `SenderPhoneNumberService` to determine the sender phone number for each message
- Logs publishing operations

#### **RabbitMqTopicSetupService.cs**
**Job:** Initializes RabbitMQ infrastructure on application startup
**Responsibilities:**
- Creates `sms_exchange` (Direct exchange type, durable)
- Creates multiple queues (`sms_queue_0`, `sms_queue_1`, etc.) based on `NumberOfTopics` configuration
- Binds queues to exchange with routing keys (`smsc.topic.0`, `smsc.topic.1`, etc.)
- Ensures all queues are durable and non-exclusive
- Logs all created resources
- Throws exception if setup fails (prevents application from starting with invalid RabbitMQ config)

#### **SenderPhoneNumberService.cs**
**Job:** Manages sender phone number selection for campaigns
**Responsibilities:**
- **GetNextPhoneNumberForCampaign()** - Returns appropriate sender phone number based on sender type:
  - `manual_number` / `manual_string`: Returns `sender_value` directly
  - `random`: Returns phone number from cached pool using round-robin (messageIndex % pool.Count)
  - Unknown types: Falls back to `sender_value` or default "0000000000"
- **GetOrCreatePhoneNumberPool()** - Caches phone number pools per campaign:
  - Uses in-memory cache with 30-minute sliding expiration
  - Creates pool if not cached
  - Returns cached pool if available
- **CreatePhoneNumberPool()** - Generates phone number pool from XML configuration:
  - Loads XML file from configured path
  - Randomly selects one range from available ranges
  - Expands range into list of all numbers (e.g., 1000-1999 → 1000, 1001, ..., 1999)
  - Shuffles the list randomly
  - Returns shuffled list
- **GetAllAvailablePhoneNumbers()** - Utility method to get all phone numbers from all ranges (for informational purposes)

**Key Features:**
- Thread-safe caching with `IMemoryCache`
- Round-robin distribution for random sender type
- XML-based configuration for phone number ranges

---

## 3. SmsGateway.Shared Project

### Purpose
Shared library containing data models, DTOs, database context, and configuration options used across multiple projects.

### Components

#### **ApplicationDbContext.cs**
**Job:** Entity Framework Core database context
**Responsibilities:**
- Defines database entities: `Campaigns`, `DeliveryDetails`, `SmscStatus`
- Configures entity relationships (Campaign 1-to-many DeliveryDetails with cascade delete)
- Configures entity properties and column mappings
- Sets up database indexes for performance:
  - Campaign: `campaign_name`, `created_at`, `status`
  - DeliveryDetails: composite index on `(campaign_id, processed)`, indexes on `status`, `phone_number`, `created_at`, `message_id`
  - SmscStatus: `updated_at`
- Configures `DeliveryStatus` enum to be stored as integer
- Configures `AdditionalData` as PostgreSQL JSONB type

#### **DesignTimeDbContextFactory.cs**
**Job:** Factory for creating DbContext during design-time operations (migrations)
**Responsibilities:**
- Implements `IDesignTimeDbContextFactory<ApplicationDbContext>`
- Provides connection string for Entity Framework migrations
- Used by `dotnet ef migrations` commands

### Models

#### **Campaign.cs**
**Job:** Entity model representing an SMS campaign
**Responsibilities:**
- Stores campaign metadata: name, message content, language
- Stores sender configuration: type (manual_number, manual_string, random), value
- Stores scheduling: type (immediate, scheduled), scheduled time
- Stores campaign settings: priority, code, provider, status, cost
- Tracks creation timestamp
- Maintains one-to-many relationship with `DeliveryDetails`

#### **DeliveryDetails.cs**
**Job:** Entity model representing individual SMS delivery record
**Responsibilities:**
- Links to parent campaign via `CampaignId` foreign key
- Stores recipient phone number
- Stores actual sender phone number used
- Stores SMSC message ID (from provider)
- Stores message content (template or actual)
- Tracks delivery status using `DeliveryStatus` enum
- Tracks processing state: `Processed`, `ProcessedAt`
- Stores error messages if delivery fails
- Tracks timestamps: `SentAt`, `DeliveredAt`, `CreatedAt`
- Stores additional dynamic recipient data as JSONB (`CustomFields` / arbitrary keys)
- Provides computed properties:
  - `IsInProgress`: True if status is Pending, Acceptable, or Accepted
  - `StatusDisplay`: User-friendly status string

#### **DeliveryStatus.cs**
**Job:** Enumeration of possible SMS delivery statuses
**Values:**
- `Pending (0)`: Message is pending processing
- `Successful (1)`: Message was successfully delivered
- `Failed (2)`: Message delivery failed
- `Accepted (3)`: Provider received the message
- `Acceptable (4)`: Message sent to SMSC, awaiting DLR
- `TimeoutSMSC (5)`: No response from SMSC within timeout
- `Unknown (6)`: Unknown status
- `Expired (7)`: No DLR received after validity period

#### **SmscStatus.cs**
**Job:** Entity model for tracking SMSC connection status
**Responsibilities:**
- Stores connection state: `IsConnected` (boolean)
- Tracks last enquire link timestamp
- Stores last error message
- Tracks last update timestamp

#### **ReceiptState.cs**
**Job:** Enumeration for delivery receipt states (used by SMSC protocol)
**Values:**
- `Unknown (0)`, `Delivered (1)`, `Expired (2)`, `Deleted (3)`, `Undeliverable (4)`, `Accepted (5)`, `Rejected (6)`

### DTOs

#### **SmsMessageDto.cs**
**Job:** Data transfer object for SMS messages published to RabbitMQ
**Properties:**
- `DeliveryId`: Database ID of the delivery detail record
- `CampaignId`: Parent campaign ID
- `PhoneNumber`: Recipient phone number
- `MessageText`: Final message text after template replacement
- `ActualSender`: Sender phone number to use

### Options

#### **RabbitMqOptions.cs**
**Job:** Configuration class for RabbitMQ settings
**Properties:**
- `Host`: RabbitMQ server hostname (default: "localhost")
- `Port`: RabbitMQ server port (default: 5672)
- `UserName`: RabbitMQ username (default: "guest")
- `Password`: RabbitMQ password (default: "guest")
- `NumberOfTopics`: Number of topic queues to create (default: 5)
- `TopicNamePrefix`: Prefix for routing keys (default: "smsc.topic")
- `MessagesPerSecond`: Rate limiting setting (default: 5)

---

## System Flow

### Campaign Creation Flow
1. Client sends POST request to `/api/campaign` with campaign details
2. `CampaignController` validates request and creates `Campaign` entity
3. For each recipient, creates `DeliveryDetails` record with phone number and additional data
4. `MessageProcessingService` processes each delivery detail:
   - Replaces template placeholders with actual values
   - Gets sender phone number from `SenderPhoneNumberService`
   - Creates `SmsMessageDto`
   - Publishes to RabbitMQ with routing key based on `campaignId % numberOfTopics`
5. Messages are queued in RabbitMQ for consumption by message sender service (not shown in this codebase)

### SMSC Server Flow
1. `SmscServerService` starts and listens on port 2775
2. Client connects and binds (Transmitter/Receiver/Transceiver)
3. Client sends `SubmitSm` with message details
4. Server generates message ID and sends `SubmitSmResp` immediately
5. Server simulates delivery after 1 second and sends `DeliverSm` (DLR) to Receiver client

---

## Technology Stack

- **.NET 9.0**: Framework version
- **Entity Framework Core 9.0**: ORM for database access
- **PostgreSQL**: Database (with JSONB support)
- **RabbitMQ**: Message broker for asynchronous message processing
- **Inetlab.SMPP 2.9.0**: SMPP protocol library for SMSC server
- **Serilog**: Structured logging framework
- **ASP.NET Core**: Web API framework

---

## Configuration Files

### appsettings.json (SmscServer)
- `SmscServer:Port`: Server listening port (default: 2775)
- `SmscServer:RequestTimeout`: Request timeout in milliseconds (default: 30000)

### appsettings.json (WebApplication1)
- `ConnectionStrings:DefaultConnection`: PostgreSQL connection string
- `RabbitMQ`: RabbitMQ connection and topic configuration
- `ConfigPaths:SenderPhoneNumberConfig`: Path to XML file with phone number ranges
- `ConfigPaths:TimingConfig`: Path to timing configuration (not used in current code)
- `Logging:LogFilePath`: Path for log file output

---

## Database Schema

### Tables
1. **campaigns**: Campaign metadata and configuration
2. **delivery_details**: Individual SMS delivery records with status tracking
3. **smsc_status**: SMSC connection status tracking

### Key Relationships
- `campaigns` 1 → N `delivery_details` (Cascade delete)

---

## Notes

- The system uses a topic-based routing strategy in RabbitMQ to distribute load across multiple queues while maintaining message order per campaign
- Phone number pools are cached per campaign to improve performance
- The SMSC server is a mock implementation for testing; it accepts all connections and simulates successful deliveries
- Additional recipient fields (`custom_fields`) are stored as JSONB in PostgreSQL for flexibility
- The system supports three sender types: manual_number, manual_string, and random (with round-robin distribution)

