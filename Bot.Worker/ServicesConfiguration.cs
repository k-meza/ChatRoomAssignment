using Microsoft.Extensions.Options;

namespace Bot.Worker;

public static class ServicesConfiguration
{
    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        
    }
    
    /// <summary>
    /// Register an <c>IOptions</c> configuration instance and also a standalone instance of the <typeparamref name="TOptions"/> class.<para/>
    /// For example, <c>ConfigureWithSelf&lt;FooSettings&gt;(...)</c> will register both <c>IOptions&lt;FooSettings&gt;</c> and <c>FooSettings</c>.
    /// </summary>
    /// <typeparam name="TOptions">Type of options to configure</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration section. Use <c>configuration.GetSection("FooSettings")</c> to get a specific named element.</param>
    static IServiceCollection AddOptionsAsSelf<TOptions>(this IServiceCollection services, IConfiguration configuration) where TOptions : class, new()
    {
        services.AddOptions<TOptions>().Bind(configuration);
        return services.AddTransient(sp => sp.GetService<IOptions<TOptions>>().Value);
    }

    /// <summary>
    /// Register a scoped service as both a <typeparamref name="TInterface"/> and a <see cref="Lazy{T}" />, bound to <typeparamref name="TType"/>.
    /// </summary>
    /// <typeparam name="TInterface">Service interface</typeparam>
    /// <typeparam name="TType">Service type</typeparam>
    /// <param name="services">Service collection</param>
    public static IServiceCollection AddScopedLazy<TInterface, TType>(this IServiceCollection services)
        where TType : class, TInterface
        where TInterface : class
    {
        return services.AddScoped<TInterface, TType>()
            .AddScoped(sp => new Lazy<TInterface>(sp.GetRequiredService<TInterface>));
    }
}