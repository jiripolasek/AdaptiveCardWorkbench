namespace AdaptiveCardWorkbench.Models;

public static class Samples
{
    public const string DefaultPayload = """
        {
          "type": "AdaptiveCard",
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "version": "1.5",
          "body": [
            {
              "type": "TextBlock",
              "text": "${title}",
              "size": "Large",
              "weight": "Bolder",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "${subtitle}",
              "isSubtle": true,
              "spacing": "Small",
              "wrap": true
            },
            {
              "type": "Container",
              "style": "emphasis",
              "spacing": "Large",
              "items": [
                {
                  "type": "ColumnSet",
                  "columns": [
                    {
                      "type": "Column",
                      "width": "stretch",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "STATUS",
                          "size": "Small",
                          "isSubtle": true
                        },
                        {
                          "type": "TextBlock",
                          "text": "${status}",
                          "weight": "Bolder",
                          "color": "Good",
                          "spacing": "Small"
                        }
                      ]
                    },
                    {
                      "type": "Column",
                      "width": "stretch",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "OWNER",
                          "size": "Small",
                          "isSubtle": true
                        },
                        {
                          "type": "TextBlock",
                          "text": "${owner}",
                          "weight": "Bolder",
                          "spacing": "Small"
                        }
                      ]
                    }
                  ]
                },
                {
                  "type": "FactSet",
                  "spacing": "Medium",
                  "facts": [
                    {
                      "title": "Due",
                      "value": "${dueDate}"
                    },
                    {
                      "title": "Environment",
                      "value": "${environment}"
                    }
                  ]
                }
              ]
            }
          ],
          "actions": [
            {
              "type": "Action.Submit",
              "title": "Approve",
              "data": {
                "action": "approve"
              }
            },
            {
              "type": "Action.OpenUrl",
              "title": "View details",
              "url": "${url}"
            }
          ]
        }
        """;

    public const string DefaultData = """
        {
          "title": "Release approval",
          "subtitle": "The Windows app is ready for production.",
          "status": "Ready",
          "owner": "Morgan Yu",
          "dueDate": "Friday, 16:00",
          "environment": "Production",
          "url": "https://adaptivecards.io"
        }
        """;
}
