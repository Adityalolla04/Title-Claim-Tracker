export interface CurrentAccount {
  id: string;
  email: string;
  displayName: string | null;
  roles: string[];
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string | null;
}
