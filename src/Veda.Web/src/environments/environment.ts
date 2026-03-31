export const environment = {
  production: false,
  msalConfig: {
    auth: {
      clientId: 'bed184b0-35d8-4048-890c-5fca4d232145',
      authority: 'https://vedaaide.ciamlogin.com/vedaaide.onmicrosoft.com',
      redirectUri: 'http://localhost:4200',
      knownAuthorities: ['vedaaide.ciamlogin.com']
    }
  },
  apiScope: 'api://5c5f20ca-86cf-47d0-bcae-883c5c1d9151/access_as_user',
  adminOids: ['494ff585-7c64-46c1-9d1e-5adacbe49be9']
};

