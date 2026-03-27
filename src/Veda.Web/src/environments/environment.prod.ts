export const environment = {
  production: true,
  msalConfig: {
    auth: {
      clientId: 'bed184b0-35d8-4048-890c-5fca4d232145',
      authority: 'https://vedaaide.ciamlogin.com/vedaaide.onmicrosoft.com',
      redirectUri: window.location.origin,
      knownAuthorities: ['vedaaide.ciamlogin.com']
    }
  },
  apiScope: 'api://5c5f20ca-86cf-47d0-bcae-883c5c1d9151/access_as_user'
};

