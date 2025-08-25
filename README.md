# Chat App with Stock Quote Bot

## Overview

This solution delivers a simple browser-based chat application using .NET 8 where registered users can chat in real-time, issue stock commands, and receive stock quotes from a decoupled bot that consumes an external CSV API and publishes the result via RabbitMQ.

### Solution Structure

- API (ASP.NET Core Web API)
  - Authentication and user management (with ASP.NET Core Identity)
  - SignalR Hub for chatrooms and real-time messaging
  - REST endpoints (e.g., Auth, Rooms, Messages)
  - Persists chat messages and chatrooms (e.g., EF Core with SQLite)
  - Publishes stock commands to RabbitMQ
- Bot.Worker (Background Worker)
  - Subscribes to stock command messages from RabbitMQ
  - Calls the CSV stock API (stooq.com) to retrieve stock data
  - Parses the CSV and publishes bot messages back to the chat via the API’s message pipeline/broker
  - Handles errors and unknown commands gracefully
- WebApp (Blazor WebAssembly)
  - Minimal front-end focused on consuming the API and SignalR for real-time chat
  - Displays chatrooms, login/register, and real-time messages
  - Shows only the last 50 messages in a chatroom (ordered by timestamp)
- Tests (NUnit)
  - Unit tests for core API and Hub logic

### Key Features

- Authentication
  - Users can register and log in to access chatrooms.
  - Option to use ASP.NET Core Identity for secure password policy and tokens.
- Real-time Chat (SignalR)
  - Authenticated users can join a chatroom and exchange messages with others.
  - The last 50 messages are loaded and shown in ascending timestamp order.
- Stock Command
  - Users can post messages in the format: /stock=stock_code
  - The server publishes a stock command message to RabbitMQ.
  - The bot retrieves quote data from: <https://stooq.com/q/l/?s={stock_code}&f=sd2t2ohlcv&h&e=csv>
  - The bot posts the response back to the chatroom in the format:
    - “APPL.US quote is $93.42 per share”
  - The stock command itself is not persisted as a user message; only the bot’s output is saved/posted.
- Messaging & Decoupling with RabbitMQ
  - The API publishes “stock.command” messages (containing stock code and room info).
  - The Bot.Worker independently consumes and responds, enabling clean separation and scalability.
- Robust Parsing and Error Handling
  - Stock command parsing is tolerant of minor formatting issues and provides helpful bot feedback on invalid input.
  - The bot logs and handles exceptions gracefully, optionally notifying the room with a friendly error message.
- Multiple Chatrooms
  - The design supports multiple chatrooms; users can join/leave rooms and receive room-scoped messages.

### Architecture & Data Flow

1) User logs in and opens a chatroom.
2) User sends a message:
    - If it’s a normal message, the API stores it and broadcasts via SignalR.
    - If it matches the stock command pattern (/stock=CODE), the API publishes a stock command on RabbitMQ and does not persist the command message as a user post.
3) Bot.Worker consumes the stock command, calls the CSV API, parses the quote, and produces a formatted bot message.
4) The API (via its messaging/broker integration) persists the bot message and broadcasts it to clients in the target room.
5) Clients see the bot’s response in near real time.

### Persistence and Ordering

- Chat messages are persisted with timestamps in UTC.
- Each chatroom retrieves the last 50 messages ordered by CreatedAtUtc ascending when a user joins.
- Bot messages are flagged distinctly to allow different styling in the UI.

### Configuration

- Database: EF Core (SQLite by default). Migrations are applied at startup.
- RabbitMQ: Configured via application configuration (e.g., appsettings.json, environment variables, or user-secrets for credentials/hostnames).
- CORS: Enabled for local dev hosts for the WebApp.

### Security & Operational Considerations

- Keep credentials and connection strings out of source control (use secrets or environment variables).
- Enforce password policies and secure cookies for authentication.
- Log with Serilog; ensure structured logging in production.

## How to Run It

This repo includes two helper scripts that boot everything in the right order (Bot → API → WebApp). Use the one that matches your OS.

> Prereqs (recap)
>
> - **.NET 8 SDK** installed
> - **RabbitMQ** running locally (Docker example):
>
>   ```bash
>   docker run -d --hostname rmq --name rmq \
>     -p 5672:5672 -p 15672:15672 rabbitmq:3-management
>   ```
>
> - (Optional) Configure credentials/hosts in `appsettings.*.json` or environment variables (e.g., `RabbitMQ:HostName`, `RabbitMQ:UserName`, `RabbitMQ:Password`).

### **_IMPORTANT:_ Configuration**

Both API and Bot.Worker use their respective `appsettings.json` files for configuration.
The .git repo is ignoring the configuration files by default so a `appsettings.json.template` file is included.`

**Create a copy (or rename it) of the template file and name it `appsettings.json`. It includes the Development configuration by default.**

This step is crucial to ensure the app can run.

### Quick Start

By default the WebApp runs in the url: `http://localhost:5149`. And the API runs in the url: `http://localhost:5199`.
If the URL and Ports change, remember to change the CORS config in the `appsettings.json` file.

#### Windows (PowerShell or CMD)

Run:

```bat
.\run.bat
```

What you’ll see:

- Three terminal windows open:

    1. **Bot.Worker** (consumes `/stock=...` commands and talks to stooq)
    2. **API** (SignalR hub + REST + publishes/consumes broker messages)
    3. **WebApp** (Blazor WebAssembly dev server)
- Each process logs its status and the **listening URLs** it bound to.
- When you’re done, **close the three windows** (or press any key in the launcher window to end it).

#### macOS / Linux

Make the script executable once:

```bash
chmod +x ./run.sh
```

Run:

```bash
./run.sh
```

What you’ll see:

- All three processes start in the current terminal.
- The script prints the **listening URLs** as each service starts.
- Press **Ctrl+C** to stop everything gracefully (the script traps the signal and kills child processes).

### Running Manually (no scripts)

If you prefer separate terminals:

```bash
# Terminal 1 (solution root)
dotnet run --project Bot.Worker/Bot.Worker.csproj

# Terminal 2
dotnet run --project API/API.csproj

# Terminal 3
dotnet run --project WebApp/WebApp.csproj
```
