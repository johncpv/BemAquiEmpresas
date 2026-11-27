// Gerado pelo OpenApiDocumentTests (INT-03 CA 24) a partir do documento integration-v1. Não edite à mão: rode BQ_UPDATE_OPENAPI=1.
export type IntegrationRoute = { method: string; path: string; scope: string | null; operationId: string | null }

export const INTEGRATION_V1_ROUTES: readonly IntegrationRoute[] = [
  {
    "method": "GET",
    "path": "/api/v1/integration/webhooks/subscription",
    "scope": null,
    "operationId": "GetIntegrationWebhookSubscription"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/connection",
    "scope": null,
    "operationId": "GetIntegrationConnection"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/customers",
    "scope": "parties.view",
    "operationId": "IntegrationSearchCustomers"
  },
  {
    "method": "POST",
    "path": "/api/v1/integration/customers",
    "scope": "parties.create",
    "operationId": "IntegrationCreateCustomer"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/customers/{id}",
    "scope": "parties.view",
    "operationId": "IntegrationGetCustomer"
  },
  {
    "method": "PATCH",
    "path": "/api/v1/integration/customers/{id}",
    "scope": "parties.edit",
    "operationId": "IntegrationPatchCustomer"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/customers/{id}/projects",
    "scope": "projects.view",
    "operationId": "IntegrationListCustomerProjects"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/customers/{id}/charges",
    "scope": "finance.view",
    "operationId": "IntegrationListCustomerCharges"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/charges/{id}",
    "scope": "finance.view",
    "operationId": "IntegrationGetCharge"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/charges/{id}/pdf",
    "scope": "finance.view",
    "operationId": "IntegrationDownloadChargePdf"
  },
  {
    "method": "POST",
    "path": "/api/v1/integration/orders",
    "scope": "sales.create",
    "operationId": "IntegrationCreateOrder"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/orders/{id}",
    "scope": "sales.view",
    "operationId": "IntegrationGetOrder"
  },
  {
    "method": "POST",
    "path": "/api/v1/integration/orders/{id}/confirm",
    "scope": "sales.create",
    "operationId": "IntegrationConfirmOrder"
  },
  {
    "method": "POST",
    "path": "/api/v1/integration/orders/{id}/charge",
    "scope": "sales.edit",
    "operationId": "IntegrationChargeOrder"
  },
  {
    "method": "POST",
    "path": "/api/v1/integration/orders/{id}/cancel",
    "scope": "sales.edit",
    "operationId": "IntegrationCancelOrder"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/orders/{id}/invoice",
    "scope": "fiscal.view",
    "operationId": "IntegrationGetOrderInvoice"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/customers/{id}/orders",
    "scope": "sales.view",
    "operationId": "IntegrationListCustomerOrders"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/invoices/{id}/danfe",
    "scope": "fiscal.view",
    "operationId": "IntegrationDownloadInvoiceDanfe"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/products",
    "scope": "catalog.view",
    "operationId": "IntegrationListProducts"
  },
  {
    "method": "GET",
    "path": "/api/v1/integration/products/{id}",
    "scope": "catalog.view",
    "operationId": "IntegrationGetProduct"
  }
]
