import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../environments/environment';

export interface AdminCustomerCatalogApiResponse {
  id: string;
  name: string;
  phoneNumber: string;
  isActive: boolean;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UpsertAdminCustomerApiRequest {
  name: string;
  phoneNumber: string | null;
  preferredDeliveryTime: string | null;
  preferredDeliveryWindowStart: string | null;
  preferredDeliveryWindowEnd: string | null;
  deliveryNotes: string | null;
}

export interface AdminProductCatalogApiResponse {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
}

export interface UpsertAdminProductApiRequest {
  name: string;
  description: string | null;
}

export interface AdminMachineCatalogApiResponse {
  id: string;
  number: number;
  name: string | null;
  isActive: boolean;
}

export interface UpsertAdminMachineApiRequest {
  number: number;
  name: string | null;
}

export interface AdminCustomerFrequentProductApiResponse {
  productId: string;
  productName: string;
  defaultQuantity: number | null;
  sortOrder: number;
  isActive: boolean;
}

export interface UpdateCustomerFrequentProductsApiRequest {
  items: UpdateCustomerFrequentProductItemApiRequest[];
}

export interface UpdateCustomerFrequentProductItemApiRequest {
  productId: string;
  defaultQuantity: number | null;
  sortOrder: number;
  isActive: boolean;
}

export interface AdminCustomerMachineAssignmentApiResponse {
  machineId: string;
  machineNumber: number;
  machineName: string | null;
  isDefault: boolean;
  isActive: boolean;
  notes: string | null;
}

export interface UpdateCustomerMachineAssignmentsApiRequest {
  items: UpdateCustomerMachineAssignmentItemApiRequest[];
}

export interface UpdateCustomerMachineAssignmentItemApiRequest {
  machineId: string;
  isDefault: boolean;
  isActive: boolean;
  notes: string | null;
}

export interface AdminCustomerAccessTokenApiResponse {
  tokenId: string;
  customerId: string;
  description: string;
  expiresAt: string | null;
  isActive: boolean;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface CreatedCustomerAccessTokenApiResponse {
  tokenId: string;
  customerId: string;
  plainToken: string;
  description: string;
  expiresAt: string | null;
  isActive: boolean;
}

export interface CreateCustomerAccessTokenApiRequest {
  description: string | null;
  expiresAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminCatalogApiService {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = environment.apiBaseUrl;

  getCustomers(): Observable<AdminCustomerCatalogApiResponse[]> {
    return this.http.get<AdminCustomerCatalogApiResponse[]>(`${this.apiBaseUrl}/api/admin/customers`);
  }

  createCustomer(request: UpsertAdminCustomerApiRequest): Observable<AdminCustomerCatalogApiResponse> {
    return this.http.post<AdminCustomerCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/customers`, request);
  }

  updateCustomer(customerId: string, request: UpsertAdminCustomerApiRequest): Observable<AdminCustomerCatalogApiResponse> {
    return this.http.put<AdminCustomerCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/customers/${customerId}`, request);
  }

  activateCustomer(customerId: string): Observable<AdminCustomerCatalogApiResponse> {
    return this.http.patch<AdminCustomerCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/customers/${customerId}/activate`, {});
  }

  deactivateCustomer(customerId: string): Observable<AdminCustomerCatalogApiResponse> {
    return this.http.patch<AdminCustomerCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/customers/${customerId}/deactivate`, {});
  }

  getProducts(): Observable<AdminProductCatalogApiResponse[]> {
    return this.http.get<AdminProductCatalogApiResponse[]>(`${this.apiBaseUrl}/api/admin/products`);
  }

  createProduct(request: UpsertAdminProductApiRequest): Observable<AdminProductCatalogApiResponse> {
    return this.http.post<AdminProductCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/products`, request);
  }

  updateProduct(productId: string, request: UpsertAdminProductApiRequest): Observable<AdminProductCatalogApiResponse> {
    return this.http.put<AdminProductCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/products/${productId}`, request);
  }

  activateProduct(productId: string): Observable<AdminProductCatalogApiResponse> {
    return this.http.patch<AdminProductCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/products/${productId}/activate`, {});
  }

  deactivateProduct(productId: string): Observable<AdminProductCatalogApiResponse> {
    return this.http.patch<AdminProductCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/products/${productId}/deactivate`, {});
  }

  getMachines(): Observable<AdminMachineCatalogApiResponse[]> {
    return this.http.get<AdminMachineCatalogApiResponse[]>(`${this.apiBaseUrl}/api/admin/machines`);
  }

  createMachine(request: UpsertAdminMachineApiRequest): Observable<AdminMachineCatalogApiResponse> {
    return this.http.post<AdminMachineCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/machines`, request);
  }

  updateMachine(machineId: string, request: UpsertAdminMachineApiRequest): Observable<AdminMachineCatalogApiResponse> {
    return this.http.put<AdminMachineCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/machines/${machineId}`, request);
  }

  activateMachine(machineId: string): Observable<AdminMachineCatalogApiResponse> {
    return this.http.patch<AdminMachineCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/machines/${machineId}/activate`, {});
  }

  deactivateMachine(machineId: string): Observable<AdminMachineCatalogApiResponse> {
    return this.http.patch<AdminMachineCatalogApiResponse>(`${this.apiBaseUrl}/api/admin/machines/${machineId}/deactivate`, {});
  }

  getFrequentProducts(customerId: string): Observable<AdminCustomerFrequentProductApiResponse[]> {
    return this.http.get<AdminCustomerFrequentProductApiResponse[]>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/frequent-products`
    );
  }

  updateFrequentProducts(
    customerId: string,
    request: UpdateCustomerFrequentProductsApiRequest
  ): Observable<AdminCustomerFrequentProductApiResponse[]> {
    return this.http.put<AdminCustomerFrequentProductApiResponse[]>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/frequent-products`,
      request
    );
  }

  getMachineAssignments(customerId: string): Observable<AdminCustomerMachineAssignmentApiResponse[]> {
    return this.http.get<AdminCustomerMachineAssignmentApiResponse[]>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/machine-assignments`
    );
  }

  updateMachineAssignments(
    customerId: string,
    request: UpdateCustomerMachineAssignmentsApiRequest
  ): Observable<AdminCustomerMachineAssignmentApiResponse[]> {
    return this.http.put<AdminCustomerMachineAssignmentApiResponse[]>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/machine-assignments`,
      request
    );
  }

  getAccessTokens(customerId: string): Observable<AdminCustomerAccessTokenApiResponse[]> {
    return this.http.get<AdminCustomerAccessTokenApiResponse[]>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/access-tokens`
    );
  }

  createAccessToken(
    customerId: string,
    request: CreateCustomerAccessTokenApiRequest
  ): Observable<CreatedCustomerAccessTokenApiResponse> {
    return this.http.post<CreatedCustomerAccessTokenApiResponse>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/access-tokens`,
      request
    );
  }

  revokeAccessToken(customerId: string, tokenId: string): Observable<AdminCustomerAccessTokenApiResponse> {
    return this.http.patch<AdminCustomerAccessTokenApiResponse>(
      `${this.apiBaseUrl}/api/admin/customers/${customerId}/access-tokens/${tokenId}/revoke`,
      {}
    );
  }
}
