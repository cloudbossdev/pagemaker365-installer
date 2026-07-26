function Get-PM365RequiredGraphScopes {
    @(
        'User.Read'
        'Domain.Read.All'
        'RoleManagement.Read.Directory'
        'Sites.Read.All'
    )
}
