#!/usr/bin/env python3
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

import os

""" Bot Configuration """


class DefaultConfig:
    """ Bot Configuration """

    PORT = 3978
    APP_ID = os.environ.get("MicrosoftAppId", "")
    APP_PASSWORD = os.environ.get("MicrosoftAppPassword", "")
    APP_TYPE = os.environ.get("MicrosoftAppType", "MultiTenant")
    APP_TENANTID = os.environ.get("MicrosoftAppTenantId", "")

    COSMOSDB_ENDPOINT = os.environ.get("AZURE_COSMOSDB_ENDPOINT", "")
    COSMOSDB_KEY = os.environ.get("AZURE_COSMOSDB_KEY", "")
    COSMOSDB_DATABASE_ID = os.environ.get("AZURE_COSMOSDB_DATABASE_ID", "")
    COSMOSDB_CONTAINER_ID = os.environ.get("AZURE_COSMOSDB_CONTAINER_ID", "")

    DEBUG = bool(os.environ.get("DEBUG", "False"))