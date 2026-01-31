using CloudinaryDotNet;
using DocumentFormat.OpenXml.Presentation;
using Microsoft.AspNetCore.Authentication;           // AuthenticationBuilder 擴充
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;    // AddGoogle 擴充方法所在組件
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using tHerdBackend.Composition;
using tHerdBackend.Core.Abstractions;
using tHerdBackend.Core.Abstractions.Referral;
using tHerdBackend.Core.Abstractions.Security;
using tHerdBackend.Core.DTOs.ORD;
using tHerdBackend.Core.DTOs.SUP.Brand;
using tHerdBackend.Core.DTOs.USER;
using tHerdBackend.Core.Interfaces.Abstractions;
using tHerdBackend.Core.Interfaces.PROD;
using tHerdBackend.Core.Interfaces.SYS;
using tHerdBackend.Infra.DBSetting;
using tHerdBackend.Infra.Helpers;
using tHerdBackend.Infra.Models;
using tHerdBackend.Infra.Repository.PROD;
using tHerdBackend.Infra.Repository.SYS;
using tHerdBackend.Services.Common;
using tHerdBackend.Services.Common.Auth;
using tHerdBackend.Services.ORD;
using tHerdBackend.Services.SUP;
using tHerdBackend.Services.USER;
using tHerdBackend.SharedApi.Controllers.Common;
using tHerdBackend.SharedApi.Infrastructure.Auth;
using tHerdBackend.SharedApi.Infrastructure.Config;
using tHerdBackend.SharedApi.Infrastructure.Email.EmailSender.cs;
using tHerdBackend.SharedApi.Infrastructure.Referral;
using tHerdBackend.SharedApi.Infrastructure.Services;
using SharedECPayService = tHerdBackend.SharedApi.Infrastructure.Services.ECPayService;




namespace tHerdBackend.SharedApi
{
    public class Program
    {
        public static void Main(string[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            // ✅ 測試：印出 ECPay 設定
            var ecpayConfig = builder.Configuration.GetSection("ECPay");
            Console.WriteLine("=== ECPay 設定檢查 ===");
            Console.WriteLine($"MerchantID: {ecpayConfig["MerchantID"]}");
            Console.WriteLine($"HashKey: {ecpayConfig["HashKey"]}");
            Console.WriteLine($"PaymentUrl: {ecpayConfig["PaymentUrl"]}");
            Console.WriteLine("=====================");

            builder.Host.ConfigureAppConfiguration((context, config) =>
            {
                if (context.HostingEnvironment.IsDevelopment())
                {
                    config.AddUserSecrets<Program>();
                }
            });

			// builder.Services.Configure<ECPaySettings>(
			//	builder.Configuration.GetSection("ECPay")
			//);

			//取得連線字串
			var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
	?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

			Console.WriteLine(
	builder.Configuration.GetConnectionString("DefaultConnection")
);

			//與後台管理系統共用 Identity 使用者資料庫
			// === Identity 使用者資料庫（與後台共用） ===
			builder.Services.AddDbContext<ApplicationDbContext>(options =>
				options.UseSqlServer(connectionString));

			builder.Services
	.AddIdentity<ApplicationUser, ApplicationRole>(options =>
	{
		// 登入前是否必須完成信箱驗證
		options.SignIn.RequireConfirmedAccount = false;   // 開發/測試可先關掉
		options.SignIn.RequireConfirmedEmail = true;

		// （可選）密碼規則
		options.Password.RequireDigit = true;
		options.Password.RequireLowercase = true;
		options.Password.RequireUppercase = true;
		options.Password.RequireNonAlphanumeric = true;
		options.Password.RequiredLength = 8;
		options.Password.RequiredUniqueChars = 1;

		// （可選）鎖定策略
		options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);
		options.Lockout.MaxFailedAccessAttempts = 5;
		options.Lockout.AllowedForNewUsers = true;

		// （可選）Email 唯一
		options.User.RequireUniqueEmail = true;
		options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
	})
	.AddEntityFrameworkStores<ApplicationDbContext>()
	.AddDefaultTokenProviders();

			// 讀取 SmtpSettings
			builder.Services.Configure<SmtpSettings>(
				builder.Configuration.GetSection("SmtpSettings"));

			// 註冊寄信服務
			builder.Services.AddTransient<IEmailSender, EmailSender>();

			// === 即時通訊（SignalR） ===
			builder.Services.AddSignalR();

			builder.Services.ConfigureExternalCookie(options =>
			{
				options.Cookie.Name = ".ExternalAuth.Temp";
				options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
				options.SlidingExpiration = false;
				options.Cookie.SameSite = SameSiteMode.None;
				options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
			});
			//關閉預設 Claims 映射
			//System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
			JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
			JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap.Clear();
			// JWT Authentication
			builder.Services.AddAuthentication(options =>
			{
				options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
			})
			.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
			{
				options.Events = new JwtBearerEvents
				{
					// ✅ 新增這段，只給 SignalR 用（不會影響現有會員 API）
					//OnMessageReceived = context =>
					//{
					//	var accessToken = context.Request.Query["access_token"];
					//	var path = context.HttpContext.Request.Path;
					//	if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
					//	{
					//		context.Token = accessToken;
					//	}
					//	return Task.CompletedTask;
					//}
					OnMessageReceived = context =>
					{
						var accessToken = context.Request.Query["access_token"];
						var path = context.HttpContext.Request.Path;
						if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chatHub"))
						{
							context.Token = accessToken;
						}
						return Task.CompletedTask;
					},

					// ✅ 自訂未授權回應（避免回 HTML）
					OnChallenge = context =>
					{
						context.HandleResponse();
						context.Response.StatusCode = StatusCodes.Status401Unauthorized;
						context.Response.ContentType = "application/json";
						return context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
					},

					// ✅ Token 驗證後補上常用 Claim
					OnTokenValidated = ctx =>
					{
						var id = ctx.Principal?.Identities.FirstOrDefault();
						if (id is not null && !id.HasClaim(c => c.Type == ClaimTypes.NameIdentifier))
						{
							var sub = id.FindFirst("sub")?.Value;
							if (!string.IsNullOrEmpty(sub))
								id.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
						}
						return Task.CompletedTask;
					}
				};

				options.MapInboundClaims = false; // 不要把標準 JWT claim 亂映射

				var cfg = builder.Configuration.GetSection("Jwt");
				var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["SigningKey"]!));
				options.TokenValidationParameters = new TokenValidationParameters
				{ // 設定驗證參數
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = key,

					ValidateIssuer = true,
					ValidIssuer = cfg["Issuer"],      // 必須 = 你簽 token 的 Issuer（tHerdBackend.AuthServer）

					ValidateAudience = true,
					ValidAudience = cfg["Audience"],  // 必須 = 你簽 token 的 Audience（tHerdFrontend.WebClient）

					ValidateLifetime = true,
					ClockSkew = TimeSpan.Zero,

					// 對齊你簽的 claims：role 用 "role"，使用者識別用 "sub"
					RoleClaimType = "role",
					NameClaimType = "sub"
				};
				//options.Events = new JwtBearerEvents // 自訂未授權回應，避免 401 回 HTML
				//{
				//	OnChallenge = context =>
				//	{
				//		context.HandleResponse();
				//		context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				//		context.Response.ContentType = "application/json";
				//		return context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
				//	},

				//	OnTokenValidated = ctx =>
				//	{
				//		var id = ctx.Principal?.Identities.FirstOrDefault();
				//		if (id is not null && !id.HasClaim(c => c.Type == ClaimTypes.NameIdentifier))
				//		{
				//			var sub = id.FindFirst("sub")?.Value;
				//			if (!string.IsNullOrEmpty(sub))
				//				id.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
				//		}
				//		return Task.CompletedTask;
				//	}
				//};


				//除錯用
				// 在 Program.cs 的 builder.Build() 之前放一次：
				//IdentityModelEventSource.ShowPII = true; // 允許輸出 PII，方便看到真錯因

				//// 你的 .AddJwtBearer(..., options => { ... })
				//options.Events = new JwtBearerEvents
				//{
				//	OnAuthenticationFailed = ctx =>
				//	{
				//		// 會看到像「IDX10214: Audience validation failed」之類的關鍵字
				//		Console.WriteLine("JWT FAILED: " + ctx.Exception);
				//		return Task.CompletedTask;
				//	},
				//	OnChallenge = context =>
				//	{
				//		// 你已經有客製 401 回應；可以加上 log
				//		Console.WriteLine("JWT CHALLENGE: " + context.ErrorDescription);
				//		return Task.CompletedTask;
				//	}
				//};
			})
			.AddGoogle("Google", options =>
			{// 3) Google OAuth（把 SignInScheme 指到外部 Cookie）
				options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
				options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
				options.CallbackPath = "/signin-google"; // 例：/auth/external/google-callback
														 //options.CallbackPath = builder.Configuration["Authentication:Google:CallbackPath"];
				options.SignInScheme = IdentityConstants.ExternalScheme; // ★ 重點：外部 Cookie
				options.SaveTokens = true;
				// options.Scope.Add("profile"); // 預設已含
				// options.Scope.Add("email");   // 預設已含
			});

			builder.Services.AddAuthorization();

			builder.Services.AddHttpContextAccessor();
			builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
			builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
			builder.Services.AddSingleton<IReferralCodeGenerator, ReferralCodeGenerator>();
			builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
			// === SQL Connection Factory ===
			builder.Services.AddScoped<ISqlConnectionFactory>(sp => new SqlConnectionFactory(connectionString));

			// === DbContext (EF Core) ===
			builder.Services.AddDbContext<tHerdDBContext>(options =>
				options.UseSqlServer(connectionString));

			// === Session（訪客用） ===
			builder.Services.AddDistributedMemoryCache();
			builder.Services.AddSession(o =>
			{
				o.Cookie.Name = ".tHerd.Session";
				o.IdleTimeout = TimeSpan.FromDays(7);
				o.Cookie.HttpOnly = true;
				o.Cookie.SameSite = SameSiteMode.Lax;
				o.Cookie.IsEssential = true;
			});

			//// 允許 CORS
			//builder.Services.AddCors(options =>
			//{
			//	options.AddPolicy("AllowAll",
			//		policy => policy
			//			.AllowAnyOrigin()
			//			.AllowAnyMethod()
			//			.AllowAnyHeader());
			//});
			builder.Services.AddCors(options =>
			{
				options.AddPolicy("AllowFrontend", policy =>
	policy
		.WithOrigins(
			"http://localhost:5173",  // Vue 前台（http）
			"https://localhost:5173", // Vue 前台（https）
			"https://localhost:7157"  // Admin 後台
		)
		.AllowAnyHeader()
		.AllowAnyMethod()
		.AllowCredentials()
);
			});

			// 前台依賴註冊Identity，註冊 CurrentUser 本體（不要掛 ICurrentUser）
			builder.Services.AddScoped<ICurrentUser, CurrentUser>();

			// 註冊 InternalApi HttpClient
			builder.Services.AddHttpClient("InternalApi", client =>
			{
				client.BaseAddress = new Uri("https://localhost:7103"); // 換成你實際後端 BaseUrl
																		// client.DefaultRequestHeaders.Add("X-Internal", "true"); // 需要可加
			});

			//前台圖片上傳註冊
			builder.Services.AddScoped<ISysAssetFileRepository, SysAssetFileRepository>();

			// Auth Service
			//builder.Services.AddScoped<AuthService>();

			// 加入 DI 註冊（這行會自動把 Infra、Service 都綁好）
			builder.Services.AddtHerdBackend(builder.Configuration);

			// Cloudinary
			builder.Services.Configure<CloudinarySettings>(
                builder.Configuration.GetSection("CloudinarySettings"));

			// 讀取設定值，建立 Cloudinary 實例
			var cloudinaryConfig = builder.Configuration.GetSection("CloudinarySettings").Get<CloudinarySettings>();
			if (cloudinaryConfig == null ||
				string.IsNullOrEmpty(cloudinaryConfig.CloudName) ||
				string.IsNullOrEmpty(cloudinaryConfig.ApiKey) ||
				string.IsNullOrEmpty(cloudinaryConfig.ApiSecret))
			{
				throw new InvalidOperationException("CloudinarySettings 未正確設定於 appsettings.json");
			}

			var account = new Account(
				cloudinaryConfig.CloudName,
				cloudinaryConfig.ApiKey,
				cloudinaryConfig.ApiSecret
			);

            // === 註冊 ShoppingCartRepository ===
            builder.Services.AddScoped<IShoppingCartRepository, ShoppingCartRepository>();


            // === 綠界設定 ===
            // 1. 金流設定 (SharedApi 使用的 ECPaySettings)
            builder.Services.Configure<ECPaySettings>(
                builder.Configuration.GetSection("ECPay"));
            builder.Services.AddScoped<SharedECPayService>();

            // 2. 金流設定 (Services/ORD 使用的 ECPayConfigDTO)
            builder.Services.Configure<ECPayConfigDTO>(
                builder.Configuration.GetSection("ECPay"));

            // 3. 物流設定 (ECPayLogisticsConfig)
            builder.Services.Configure<ECPayLogisticsConfig>(
                builder.Configuration.GetSection("ECPay:Logistics"));
            builder.Services.AddScoped<ECPayLogisticsService>();

            // 4. 訂單計算服務
            builder.Services.AddScoped<IOrderCalculationService, OrderCalculationService>();

            // 註冊 Cloudinary 為 Singleton
            var cloudinary = new Cloudinary(account);
			builder.Services.AddSingleton(cloudinary);

			builder.Services.AddScoped<IImageStorage, CloudinaryImageStorage>();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            // Controllers & Swagger
            builder.Services.AddControllers(options =>
            {
                // ➜ 設定全域 Controller 行為，例如加入自訂路由規則
                options.Conventions.Add(new FolderBasedRouteConvention());
            })
            .AddJsonOptions(o =>
            {
                // ➜ 設定 JSON 序列化規則，例如忽略屬性大小寫
                o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
				//確認轉換成的 JSON 屬性名稱和 DTO 類別的屬性名稱完全相同（大小寫皆相同）:PascalCase
				//o.JsonSerializerOptions.PropertyNamingPolicy = null;       
				//o.JsonSerializerOptions.DictionaryKeyPolicy = null;         
			});

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "tHerd API", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "輸入 JWT Token，格式: Bearer {token}",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        new string[]{}
                    }
                });
            });

            var app = builder.Build();


            // === 啟用 Session ===
            app.UseSession();
			
            app.UseMiddleware<ProblemDetailsExceptionMiddleware>();

			// Configure the HTTP request pipeline.
			if (app.Environment.IsDevelopment())
            {
				app.UseSwagger();
                app.UseSwaggerUI();
            }

            

                        app.UseHttpsRedirection();

            

            

            			// 訪客追蹤：若無 guestId 就建立
            			app.Use(async (ctx, next) =>

            			{

            				if (string.IsNullOrEmpty(ctx.Session.GetString("guestId")))

            					ctx.Session.SetString("guestId", Guid.NewGuid().ToString("N"));

            				await next();

            			});

            

            			app.UseCors("AllowFrontend");   // 確保這行存在且在 UseAuthorization 之前

            								   // 啟用 JWT 驗證

            			app.UseAuthentication();

            
			app.UseAuthorization();

			app.MapControllers();

			// === 即時通訊（SignalR Hub） ===

			app.MapHub<tHerdBackend.SharedApi.Hubs.ChatHub>("/chatHub");
			//app.MapHub<tHerdBackend.SharedApi.Hubs.ChatHub>("/frontHub");

			app.Run();

        }
    }
}
