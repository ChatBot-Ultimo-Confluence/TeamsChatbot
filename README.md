# Confluence ChatBot

This repository contains the source code for a chatbot integration with Confluence.

## ⚠️ Important Notice

The file `appsettings.json` is **excluded** from the repository because it contains sensitive configuration information such as API tokens.

### To run the project locally:

1. Create your own `appsettings.json` in the `bin/Debug/net8.0/` directory or appropriate runtime path.
2. Use the following template:

```json
{
  "Confluence": {
    "BaseUrl": "https://your-confluence-instance.atlassian.net/wiki",
    "UserEmail": "your@email.com",
    "TokenApi": "your-secret-api-token"
  }
}