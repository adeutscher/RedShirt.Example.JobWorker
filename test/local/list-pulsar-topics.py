#!/usr/bin/env python

import requests
import json

def get_pulsar_topics(tenant="public", namespace="default", host="localhost", port=8080):
    """
    Fetches both persistent and non-persistent topics for a given Pulsar namespace.
    """
    base_url = f"http://{host}:{port}/admin/v2"
    headers = {"Content-Type": "application/json"}
    
    # Target endpoints for topic discovery
    endpoints = {
        "Persistent": f"{base_url}/namespaces/{tenant}/{namespace}/topics",
        "Non-Persistent": f"{base_url}/non-persistent/{tenant}/{namespace}"
    }
    
    all_topics = {}

    for topic_type, url in endpoints.items():
        try:
            response = requests.get(url, headers=headers)
            
            if response.status_code == 200:
                all_topics[topic_type] = response.json()
            elif response.status_code == 404:
                all_topics[topic_type] = []  # Namespace or type doesn't exist yet
            else:
                all_topics[topic_type] = f"Error {response.status_code}: {response.text}"
                
        except requests.exceptions.ConnectionError:
            print(f"CRITICAL: Unable to connect to Pulsar Admin interface at {base_url}.")
            print("Please ensure your local standalone Pulsar server is running.")
            return None

    return all_topics

if __name__ == "__main__":
    # Query default standalone namespace (public/default)
    topics = get_pulsar_topics(tenant="public", namespace="default")
    
    if topics:
        print(json.dumps(topics, indent=4))
