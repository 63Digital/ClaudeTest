# YARP Proxy Service with Hybrid Browser Rendering

A scalable reverse proxy service built with YARP (Yet Another Reverse Proxy) that intelligently routes traffic through an external proxy provider to access location-blocked websites. Features both fast URL rewriting for simple content and headless browser rendering for complex JavaScript applications.

## Features

### Core Features
- **Reverse Proxy**: Built on Microsoft's YARP framework for high-performance proxying
- **External Proxy Support**: Route traffic through HTTP/HTTPS proxy servers
- **Hybrid Routing**: Intelligent routing between fast path (URL rewriting) and browser path (headless Chrome)
- **Browser Pool**: Managed pool of headless Chrome instances for JavaScript-heavy sites
- **URL Rewriting**: Automatically rewrites URLs in responses to maintain proxy transparency
- **Content Type Handling**: Smart rewriting for HTML, CSS, JavaScript, and JSON content
- **Redirect Handling**: Properly rewrites Location headers for 301/302/307/308 redirects
- **Session Management**: Browser instance reuse for better performance
- **Configurable**: Easy configuration via `appsettings.json`
- **Authentication**: Supports proxy authentication with username/password
- **Logging**: Comprehensive logging for debugging and monitoring

### Hybrid Architecture

The service supports two rendering paths:

**Fast Path (YARP URL Rewriting)**
- For static content and simple HTML pages
- Very low latency (<100ms overhead)
- Handles: CSS, JS, images, simple server-rendered pages
- Scales to thousands of concurrent users

**Browser Path (Headless Chrome)**
- For complex JavaScript applications (React, Vue, Angular)
- Perfect rendering accuracy
- Handles: SPAs, dynamic content, AJAX-heavy sites
- Managed browser instance pool

## Architecture

```
                    Client Request
                         ↓
              Intelligent Router
                    ↓         ↓
         Fast Path          Browser Path
         (YARP Rewriting)   (Puppeteer)
              ↓                  ↓
         External Proxy ← → Browser Pool
              ↓                  ↓
         Blocked Website   Blocked Website
              ↓                  ↓
         URL Rewriting     Rendered Content
              ↓                  ↓
              Client Response
```

## Prerequisites

- .NET 8.0 SDK or later
- An external HTTP/HTTPS proxy server (if accessing blocked content)

## Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd ClaudeTest
```

2. Restore dependencies:
```bash
cd YarpProxyService
dotnet restore
```

3. Configure your settings in `appsettings.json` (see Configuration section below)

4. Run the application:
```bash
dotnet run
```

The service will start on `http://localhost:5000` by default (or the configured port).

## Configuration

### Basic Configuration

Edit `appsettings.json` to configure the proxy service:

```json
{
  "Proxy": {
    "Uri": "http://your-proxy-server:8080",
    "Username": "proxy-username",
    "Password": "proxy-password",
    "Type": "Http",
    "TimeoutSeconds": 30
  },
  "ReverseProxy": {
    "Routes": {
      "default-route": {
        "ClusterId": "default-cluster",
        "Match": {
          "Path": "{**catch-all}"
        }
      }
    },
    "Clusters": {
      "default-cluster": {
        "Destinations": {
          "primary": {
            "Address": "https://blocked-website.com"
          }
        }
      }
    }
  },
  "UrlRewriting": {
    "Enabled": true,
    "RewriteContentTypes": [
      "text/html",
      "text/css",
      "application/javascript",
      "text/javascript",
      "application/json"
    ]
  },
  "Scaling": {
    "FastPath": {
      "Enabled": true
    },
    "BrowserPath": {
      "Enabled": true
    }
  },
  "BrowserPool": {
    "MinSize": 2,
    "MaxSize": 10
  }
}
```

### Browser Path Configuration

To enable headless browser rendering for JavaScript-heavy sites:

```json
{
  "Scaling": {
    "BrowserPath": {
      "Enabled": true  // Enable headless browser path
    }
  },
  "BrowserPool": {
    "MinSize": 2,      // Minimum browsers to keep ready
    "MaxSize": 10      // Maximum concurrent browsers
  }
}
```

**When to enable Browser Path:**
- Target website is a Single Page Application (React, Vue, Angular)
- Content is heavily JavaScript-dependent
- Fast path (URL rewriting) doesn't work properly
- You need perfect rendering accuracy

**Performance Considerations:**
- Each browser instance uses ~200MB RAM
- Browser path has higher latency (~2s vs ~100ms)
- Pool automatically manages browser lifecycle
- Browsers are reused across sessions for efficiency

### Configuration Options

#### Proxy Settings

- **Uri**: The address of your external proxy server (required)
- **Username**: Username for proxy authentication (optional)
- **Password**: Password for proxy authentication (optional)
- **Type**: Proxy type - `Http`, `Https`, or `Socks5` (currently only Http/Https supported)
- **TimeoutSeconds**: Connection timeout in seconds (default: 30)

#### Reverse Proxy Settings

- **Address**: The destination website you want to access through the proxy

#### URL Rewriting Settings

- **Enabled**: Enable/disable URL rewriting (default: true)
- **RewriteContentTypes**: List of content types to rewrite

### Environment Variables

You can override configuration using environment variables:

```bash
export Proxy__Uri="http://proxy-server:8080"
export Proxy__Username="username"
export Proxy__Password="password"
export ReverseProxy__Clusters__default-cluster__Destinations__primary__Address="https://example.com"
```

## Usage

### Basic Usage

1. Start the service:
```bash
dotnet run
```

2. Access the proxied website through your local server:
```
http://localhost:5000/
```

All requests will be routed through your configured proxy to the destination website.

### Understanding Routing Decisions

The service automatically decides whether to use fast path or browser path based on:

**Fast Path is used for:**
- Static files (.css, .js, .jpg, .png, etc.)
- Simple HTML pages
- API endpoints
- When browser pool is at capacity (fallback)

**Browser Path is used for:**
- SPA routes (/, /app, /dashboard, /admin)
- Requests with framework indicators (?react, ?vue, ?angular)
- Paths without file extensions at shallow depth

You can check which path was used by looking at response headers:
```bash
curl -I http://localhost:5000/

HTTP/1.1 200 OK
X-Proxy-Path: fast
X-Proxy-Reason: Static content - using fast path
```

### Multiple Destinations

To proxy multiple websites, you can add additional routes and clusters:

```json
"ReverseProxy": {
  "Routes": {
    "site1-route": {
      "ClusterId": "site1-cluster",
      "Match": {
        "Hosts": ["site1.localhost"]
      }
    },
    "site2-route": {
      "ClusterId": "site2-cluster",
      "Match": {
        "Hosts": ["site2.localhost"]
      }
    }
  },
  "Clusters": {
    "site1-cluster": {
      "Destinations": {
        "primary": {
          "Address": "https://website1.com"
        }
      }
    },
    "site2-cluster": {
      "Destinations": {
        "primary": {
          "Address": "https://website2.com"
        }
      }
    }
  }
}
```

## Project Structure

```
YarpProxyService/
├── Program.cs                              # Main entry point
├── appsettings.json                        # Configuration
├── appsettings.Development.json            # Development configuration
├── YarpProxyService.csproj                 # Project file
├── Proxy/
│   ├── ProxyHttpClientFactory.cs          # HTTP client with proxy support
│   └── Configuration/
│       └── ProxySettings.cs               # Proxy configuration model
├── Transforms/
│   ├── ResponseRewriteTransform.cs        # URL rewriting logic
│   └── ResponseRewriteTransformProvider.cs # Transform registration
├── BrowserPool/
│   ├── BrowserInstance.cs                 # Browser instance model
│   ├── BrowserPoolManager.cs              # Browser pool manager
│   └── BrowserFetchService.cs             # Browser fetch service
├── Routing/
│   ├── RouteDecision.cs                   # Route decision model
│   └── IntelligentRequestRouter.cs        # Fast/browser path router
└── Middleware/
    └── HybridRoutingMiddleware.cs         # Routing middleware
```

## How It Works

### 1. Request Flow (Hybrid Routing)

1. Client sends a request to the YARP service (e.g., `http://localhost:5000/page`)
2. `HybridRoutingMiddleware` intercepts the request
3. `IntelligentRequestRouter` analyzes the request and decides routing path:
   - **Fast Path**: For static files, simple HTML, API endpoints
   - **Browser Path**: For SPAs, JavaScript-heavy pages

**Fast Path Flow:**
4a. YARP applies request transforms (sets X-Forwarded headers)
5a. `ProxyHttpClientFactory` creates HTTP client configured with external proxy
6a. Request is forwarded through external proxy to destination
7a. `ResponseRewriteTransform` rewrites URLs in the response
8a. Modified response sent to client

**Browser Path Flow:**
4b. Session ID is retrieved or created
5b. Browser instance is acquired from the pool (or created if needed)
6b. Puppeteer navigates to the URL through configured proxy
7b. Page waits for network idle and JavaScript execution
8b. Rendered HTML is captured
9b. URLs are rewritten in the rendered content
10b. Modified content sent to client
11b. Browser instance returned to pool for reuse

### 2. Browser Pool Management

The `BrowserPoolManager` maintains a pool of headless Chrome instances:

- **Initialization**: Creates minimum number of browsers on startup
- **Acquisition**: Assigns browsers to sessions (reuses if session exists)
- **Release**: Returns browsers to pool after cleaning state
- **Maintenance**: Every 2 minutes:
  - Cleans up idle sessions (>15 minutes)
  - Removes unhealthy browsers
  - Maintains minimum pool size

### 3. URL Rewriting

The service rewrites URLs in the following patterns:

- **Absolute URLs**: `https://destination.com/path` → `http://localhost:5000/path`
- **Protocol-relative URLs**: `//destination.com/path` → `//localhost:5000/path`
- **Location Headers**: Redirects are rewritten to keep the client proxied

This works in both fast path (transform-based) and browser path (regex-based).

## Troubleshooting

### Common Issues

#### 1. Connection to Proxy Failed

**Error**: `Error connecting to proxy server`

**Solution**:
- Verify the proxy URI is correct
- Check that the proxy server is accessible
- Verify authentication credentials if required

#### 2. SSL/TLS Errors

**Error**: `SSL connection could not be established`

**Solution**:
- Ensure the destination URL uses the correct protocol (http vs https)
- Check if the proxy supports HTTPS tunneling

#### 3. URLs Not Being Rewritten

**Problem**: Links still point to the destination website

**Solution**:
- Check that `UrlRewriting.Enabled` is `true`
- Verify the content type is in the `RewriteContentTypes` list
- Check the logs for rewriting errors
- Some JavaScript-generated URLs may not be rewritable

#### 4. Proxy Authentication Failed

**Error**: `407 Proxy Authentication Required`

**Solution**:
- Verify the username and password are correct
- Check that the credentials are properly configured

#### 5. Browser Pool Exhausted

**Error**: `Browser pool at capacity. Try again later.`

**Solution**:
- Increase `BrowserPool:MaxSize` in configuration
- Check if browsers are being released properly (check logs)
- Reduce idle timeout for unused sessions
- Consider disabling browser path for some routes

#### 6. Chromium Download Failed

**Error**: `Failed to download Chromium`

**Solution**:
- Ensure internet connectivity
- Check firewall/proxy settings
- PuppeteerSharp will auto-download Chromium on first run
- You may need to run `dotnet run` twice the first time

#### 7. Browser Crashes or Becomes Unhealthy

**Problem**: Browsers keep crashing or becoming unhealthy

**Solution**:
- Check available system memory (each browser uses ~200MB)
- Reduce `BrowserPool:MaxSize`
- Check logs for specific error messages
- Ensure Chrome dependencies are installed (Linux):
  ```bash
  apt-get install -y libnss3 libatk1.0-0 libatk-bridge2.0-0 \
    libcups2 libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 \
    libxrandr2 libgbm1 libasound2
  ```

### Debug Mode

For detailed logging, set the logging level in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Yarp": "Debug",
      "YarpProxyService": "Debug"
    }
  }
}
```

## Health Checks

The service exposes a health check endpoint:

```
GET /health
```

Returns `200 OK` if the service is running.

## Limitations

- **JavaScript-generated URLs**: Some URLs created dynamically by JavaScript may not be rewritable
- **WebSockets**: WebSocket connections require additional implementation (future enhancement)
- **CORS**: Some websites may have CORS restrictions that affect proxying
- **Binary Content**: Images, PDFs, and other binary content are passed through without modification

## Security Considerations

- Store proxy credentials securely (use User Secrets in development, environment variables in production)
- Never commit credentials to version control
- The service removes `Content-Security-Policy` and `X-Frame-Options` headers to allow proxying
- Consider implementing rate limiting to prevent abuse
- Use HTTPS for the YARP service in production

## Performance

- Handles concurrent requests efficiently
- Response buffering may increase memory usage for large responses
- URL rewriting adds minimal overhead (< 500ms typically)

## Future Enhancements

- [ ] SOCKS5 proxy support
- [ ] Multiple proxy fallback/rotation
- [ ] WebSocket support
- [ ] Response caching
- [ ] Admin API for runtime configuration
- [ ] Request/response logging with sanitization
- [ ] Docker container support

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## License

[Specify your license here]

## Acknowledgments

- Built with [YARP](https://microsoft.github.io/reverse-proxy/) by Microsoft
- Uses [ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/)
