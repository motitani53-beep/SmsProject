# Transmitter Microservice

A stateless SMPP bridge service that consumes messages from RabbitMQ and forwards them to an SMSC server via SMPP protocol.

## Overview

This microservice acts as a bridge between RabbitMQ message queues and an SMSC (Short Message Service Center) server. It:

1. Connects to an SMSC server via SMPP protocol as a Transmitter
2. Consumes messages from RabbitMQ queue `sms_messages`
3. Forwards messages to SMSC via SMPP SubmitSm
4. Publishes responses to RabbitMQ queue `smsc_results`

## Features

- **SMPP Connection Management**: Maintains persistent connection to SMSC with automatic binding
- **EnquireLink**: Performs periodic link health checks every 2 minutes
- **RabbitMQ Integration**: Consumes from `sms_messages` queue and publishes to `smsc_results` queue
- **Message Correlation**: Maps `DeliveryId` to SMPP Sequence number for tracking
- **Error Handling**: Requeues messages on failure, only acknowledges after successful SMSC submission
- **Structured Logging**: Uses Serilog for comprehensive logging

## Configuration

Edit `appsettings.json` to configure:

### SMPP Settings
```json
{
  "Smpp": {
    "Host": "localhost",
    "Port": 2775,
    "SystemId": "SMSC_MOCK",
    "Password": "password",
    "EnquireLinkIntervalSeconds": 120
  }
}
```

### RabbitMQ Settings
```json
{
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "InputQueue": "sms_messages",
    "OutputQueue": "smsc_results"
  }
}
```

## Message Format

### Input Message (sms_messages queue)
```json
{
  "type": "SendRequest",
  "delivery_id": 123,
  "campaign_id": 45,
  "phone_number": "1234567890",
  "message_text": "Hello World",
  "actual_sender": "9876543210"
}
```

### Output Message (smsc_results queue)
```json
{
  "type": "SubmitSmResp",
  "delivery_id": 123,
  "smsc_message_id": "abc123def456",
  "status": "ESME_ROK"
}
```

## Running the Service

```bash
cd TransmitterMicroservice/TransmitterMicroservice
dotnet run
```

## Dependencies

- .NET 9.0
- Inetlab.SMPP 2.9.0
- RabbitMQ.Client 6.8.1
- Serilog for logging

## Architecture

- **TransmitterService**: Main background service implementing the bridge logic
- **SendRequestDto**: DTO for incoming messages from RabbitMQ
- **SubmitSmRespDto**: DTO for outgoing responses to RabbitMQ
- **SmppOptions**: Configuration for SMPP connection
- **RabbitMqOptions**: Configuration for RabbitMQ connection

## Key Behaviors

1. **Guard Clause**: Messages are requeued if SMPP client is not bound
2. **Type Filtering**: Only processes messages with `Type == "SendRequest"`
3. **Correlation**: `DeliveryId` is mapped to SMPP `Sequence` number
4. **Acknowledgment**: RabbitMQ messages are only acknowledged after successful SMPP submission
5. **Response Publishing**: All SubmitSmResp responses are published to `smsc_results` queue via event handler

