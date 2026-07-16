using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_values_json = table.Column<string>(type: "jsonb", nullable: true),
                    new_values_json = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    nationality_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employment_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    work_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    probation_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_employees_manager_id",
                        column: x => x.manager_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feature_access_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantee_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    grantee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_access_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gdpr_consent_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    consented = table.Column<bool>(type: "boolean", nullable: false),
                    consented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gdpr_consent_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invitation_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invited_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    invited_full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_with = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completion_methods_json = table.Column<string>(type: "jsonb", nullable: true),
                    allow_google_email_mismatch = table.Column<bool>(type: "boolean", nullable: true),
                    allowed_email_domains_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "module_catalog",
                columns: table => new
                {
                    module_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    pillar = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    phase = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pricing_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    price_brackets = table.Column<string>(type: "jsonb", nullable: false),
                    full_license_price = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    maintenance_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_catalog", x => x.module_key);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    feature_limits_json = table.Column<string>(type: "jsonb", nullable: true),
                    included_modules_json = table.Column<string>(type: "jsonb", nullable: true),
                    company_size_range = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pricing_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    calculated_monthly_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    calculated_annual_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    override_monthly_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    override_annual_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ai_token_limit_per_month = table.Column<int>(type: "integer", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_auth_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    password_completion_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    google_completion_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    google_email_mismatch_default = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_login_domains_json = table.Column<string>(type: "text", nullable: true),
                    mfa_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_auth_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_status_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_status_histories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    current_period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    payment_provider_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    commercial_model = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    billing_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subscription_collection_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    gateway_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    gateway_customer_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gateway_subscription_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    license_payment_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    full_license_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    license_paid_at = table.Column<DateOnly>(type: "date", nullable: true),
                    license_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    maintenance_collection_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    maintenance_billing_cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    contract_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    contract_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    maintenance_start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_renewal_date = table.Column<DateOnly>(type: "date", nullable: true),
                    maintenance_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    maintenance_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    company_size_range = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    selected_modules = table.Column<string>(type: "jsonb", nullable: false),
                    calculated_monthly_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    calculated_annual_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    override_monthly_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    override_annual_price = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ai_token_limit_per_month = table.Column<int>(type: "integer", nullable: true),
                    custom_contract_value = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    industry_profile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    company_size_range = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    subscription_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settings_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_external_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    provider_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_external_identities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    secret = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    password_set_by_admin = table.Column<bool>(type: "boolean", nullable: false),
                    temporary_password_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    password_reset_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    password_reset_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_permission_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "module_catalog",
                columns: new[] { "module_key", "created_at", "full_license_price", "is_active", "maintenance_rate", "name", "phase", "pillar", "price_brackets", "pricing_unit", "updated_at" },
                values: new object[,]
                {
                    { "chat_ai", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Chat AI", "phase_1", "ai", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":2.0,\"annual_price\":20.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":1.5,\"annual_price\":15.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":1.0,\"annual_price\":10.0}]", "per_employee", null },
                    { "core_hr", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Core HR", "phase_1", "core_hr", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":4.0,\"annual_price\":40.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":3.5,\"annual_price\":35.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":3.0,\"annual_price\":30.0}]", "per_employee", null },
                    { "work_management", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, null, "Work Management", "phase_1", "work_management", "[{\"min_employees\":0,\"max_employees\":50,\"monthly_price\":4.5,\"annual_price\":45.0},{\"min_employees\":51,\"max_employees\":200,\"monthly_price\":4.0,\"annual_price\":40.0},{\"min_employees\":201,\"max_employees\":-1,\"monthly_price\":3.5,\"annual_price\":35.0}]", "per_employee", null }
                });

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "id", "ai_token_limit_per_month", "calculated_annual_price", "calculated_monthly_price", "code", "company_size_range", "created_at", "currency", "feature_limits_json", "included_modules_json", "is_active", "name", "override_annual_price", "override_monthly_price", "pricing_unit", "tier", "updated_at" },
                values: new object[] { new Guid("a1b2c3d4-0001-0001-0001-000000000001"), null, 75.00m, 7.50m, "starter_51_200", "51-200", new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", null, "[\"core_hr\",\"work_management\"]", true, "Starter - 51-200", null, null, "per_employee", "starter", null });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_created_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_created_at",
                table: "audit_logs",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_manager_id",
                table: "employees",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_tenant_id_employee_number",
                table: "employees",
                columns: new[] { "tenant_id", "employee_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                table: "employees",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feature_access_grants_tenant_id_grantee_type_grantee_id_mod",
                table: "feature_access_grants",
                columns: new[] { "tenant_id", "grantee_type", "grantee_id", "module" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_consent_records_tenant_id_user_id_consent_type",
                table: "gdpr_consent_records",
                columns: new[] { "tenant_id", "user_id", "consent_type" });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_expires_at",
                table: "invitation_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_tenant_id_invited_email",
                table: "invitation_tokens",
                columns: new[] { "tenant_id", "invited_email" });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_tokens_token_hash",
                table: "invitation_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id_tenant_id",
                table: "password_reset_tokens",
                columns: new[] { "user_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_id",
                table: "refresh_tokens",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_tenant_id",
                table: "sessions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_is_revoked",
                table: "sessions",
                columns: new[] { "user_id", "is_revoked" });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_code",
                table: "subscription_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_auth_policies_tenant_id",
                table: "tenant_auth_policies",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_status_histories_changed_at",
                table: "tenant_status_histories",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_status_histories_tenant_id",
                table: "tenant_status_histories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_tenant_id",
                table: "tenant_subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_external_identities_tenant_id_provider_provider_subject",
                table: "user_external_identities",
                columns: new[] { "tenant_id", "provider", "provider_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_external_identities_user_id",
                table: "user_external_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_mfa_user_id_method_type_is_verified",
                table: "user_mfa",
                columns: new[] { "user_id", "method_type", "is_verified" });

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_permission_id",
                table: "user_permission_overrides",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_tenant_id_user_id_permission_id",
                table: "user_permission_overrides",
                columns: new[] { "tenant_id", "user_id", "permission_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id",
                table: "user_roles",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_tenant_id_email",
                table: "users",
                columns: new[] { "tenant_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "feature_access_grants");

            migrationBuilder.DropTable(
                name: "gdpr_consent_records");

            migrationBuilder.DropTable(
                name: "invitation_tokens");

            migrationBuilder.DropTable(
                name: "module_catalog");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "tenant_auth_policies");

            migrationBuilder.DropTable(
                name: "tenant_status_histories");

            migrationBuilder.DropTable(
                name: "tenant_subscriptions");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "user_external_identities");

            migrationBuilder.DropTable(
                name: "user_mfa");

            migrationBuilder.DropTable(
                name: "user_permission_overrides");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
