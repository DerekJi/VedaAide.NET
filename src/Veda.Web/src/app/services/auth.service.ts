import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { MsalBroadcastService, MsalService } from '@azure/msal-angular';
import { AccountInfo, EventMessage, EventType, InteractionStatus } from '@azure/msal-browser';
import { Subject } from 'rxjs';
import { filter, takeUntil } from 'rxjs/operators';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly msal = inject(MsalService);
  private readonly broadcast = inject(MsalBroadcastService);
  private readonly destroy$ = new Subject<void>();

  private readonly _account = signal<AccountInfo | null>(null);
  private readonly _interactionInProgress = signal(false);

  readonly isLoggedIn = computed(() => this._account() !== null);
  readonly userName = computed(() => this._account()?.name ?? this._account()?.username ?? null);
  readonly userEmail = computed(() => this._account()?.username ?? null);

  constructor() {
    this.broadcast.inProgress$
      .pipe(
        filter(s => s === InteractionStatus.None),
        takeUntil(this.destroy$)
      )
      .subscribe(() => this._account.set(this.msal.instance.getActiveAccount()));

    this.broadcast.msalSubject$
      .pipe(
        filter((msg: EventMessage) =>
          msg.eventType === EventType.LOGIN_SUCCESS ||
          msg.eventType === EventType.ACCOUNT_ADDED ||
          msg.eventType === EventType.LOGOUT_SUCCESS
        ),
        takeUntil(this.destroy$)
      )
      .subscribe(() => this._account.set(this.msal.instance.getActiveAccount()));

    // Set initial account
    this._account.set(this.msal.instance.getActiveAccount() ?? this.msal.instance.getAllAccounts()[0] ?? null);
  }

  login(): void {
    this.msal.loginRedirect({
      scopes: [environment.apiScope],
      prompt: 'select_account'
    });
  }

  logout(): void {
    const account = this._account();
    this.msal.logoutRedirect({ account: account ?? undefined });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
