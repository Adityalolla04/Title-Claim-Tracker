import { Injectable, computed, signal } from '@angular/core';
import { CurrentAccount } from '../models/auth.model';

@Injectable({ providedIn: 'root' })
export class AuthStateService {
  private readonly accountValue = signal<CurrentAccount | null | undefined>(undefined);
  private readonly errorValue = signal<string | null>(null);

  readonly account = this.accountValue.asReadonly();
  readonly error = this.errorValue.asReadonly();
  readonly isRestoring = computed(() => this.accountValue() === undefined);
  readonly isAuthenticated = computed(() => this.accountValue() !== undefined && this.accountValue() !== null);
  readonly isAdministrator = computed(() => this.accountValue()?.roles.includes('Admin') === true);

  setAccount(account: CurrentAccount): void {
    this.errorValue.set(null);
    this.accountValue.set(account);
  }

  clear(): void {
    this.errorValue.set(null);
    this.accountValue.set(null);
  }

  markUnavailable(message: string): void {
    this.errorValue.set(message);
    this.accountValue.set(null);
  }
}
