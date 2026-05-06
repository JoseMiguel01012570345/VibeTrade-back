using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeTrade.Backend.Data.Entities;

namespace VibeTrade.Backend.Data.Configurations;

public sealed class TradeAgreementServiceItemRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceItemRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceItemRow> e)
    {
        e.ToTable("trade_agreement_service_items");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(80);
        e.Property(x => x.TradeAgreementId).HasMaxLength(64);
        e.Property(x => x.LinkedStoreServiceId).HasMaxLength(64);
        e.Property(x => x.TipoServicio).HasMaxLength(512);
        e.Property(x => x.TiempoStartDate).HasMaxLength(64);
        e.Property(x => x.TiempoEndDate).HasMaxLength(64);
        e.Property(x => x.Descripcion).HasColumnType("text");
        e.Property(x => x.Incluye).HasColumnType("text");
        e.Property(x => x.NoIncluye).HasColumnType("text");
        e.Property(x => x.Entregables).HasColumnType("text");
        e.Property(x => x.MetodoPago).HasMaxLength(256);
        e.Property(x => x.Moneda).HasMaxLength(128);
        e.Property(x => x.MedicionCumplimiento).HasColumnType("text");
        e.Property(x => x.PenalIncumplimiento).HasColumnType("text");
        e.Property(x => x.NivelResponsabilidad).HasColumnType("text");
        e.Property(x => x.PropIntelectual).HasColumnType("text");
        e.Property(x => x.CondicionesExtrasJson).HasColumnType("text");
        e.Property(x => x.ScheduleDefaultWindowStart).HasMaxLength(16);
        e.Property(x => x.ScheduleDefaultWindowEnd).HasMaxLength(16);
        e.Property(x => x.GarantiasTexto).HasColumnType("text");
        e.Property(x => x.PenalAtrasoTexto).HasColumnType("text");
        e.Property(x => x.TerminacionAvisoDias).HasMaxLength(64);
        e.HasIndex(x => x.TradeAgreementId);
        e.HasMany(x => x.ScheduleMonths).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.ScheduleDays).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.ScheduleOverrides).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.PaymentMonths).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.PaymentEntries).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.RiesgoItems).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.DependenciaItems).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.TerminacionCausas).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.MonedasAceptadas).WithOne(x => x.ServiceItem).HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TradeAgreementServiceScheduleMonthRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceScheduleMonthRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceScheduleMonthRow> e)
    {
        e.ToTable("trade_agreement_service_schedule_months");
        e.HasKey(x => new { x.ServiceItemId, x.Month });
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
    }
}

public sealed class TradeAgreementServiceScheduleDayRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceScheduleDayRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceScheduleDayRow> e)
    {
        e.ToTable("trade_agreement_service_schedule_days");
        e.HasKey(x => new { x.ServiceItemId, x.Month, x.CalendarDay });
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
    }
}

public sealed class TradeAgreementServiceScheduleOverrideRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceScheduleOverrideRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceScheduleOverrideRow> e)
    {
        e.ToTable("trade_agreement_service_schedule_overrides");
        e.HasKey(x => new { x.ServiceItemId, x.Month, x.CalendarDay });
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
        e.Property(x => x.WindowStart).HasMaxLength(16);
        e.Property(x => x.WindowEnd).HasMaxLength(16);
    }
}

public sealed class TradeAgreementServicePaymentMonthRowConfiguration : IEntityTypeConfiguration<TradeAgreementServicePaymentMonthRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServicePaymentMonthRow> e)
    {
        e.ToTable("trade_agreement_service_payment_months");
        e.HasKey(x => new { x.ServiceItemId, x.Month });
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
    }
}

public sealed class TradeAgreementServicePaymentEntryRowConfiguration : IEntityTypeConfiguration<TradeAgreementServicePaymentEntryRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServicePaymentEntryRow> e)
    {
        e.ToTable("trade_agreement_service_payment_entries");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
        e.Property(x => x.Amount).HasMaxLength(64);
        e.Property(x => x.Moneda).HasMaxLength(32);
        e.HasIndex(x => x.ServiceItemId);
    }
}

public sealed class TradeAgreementServiceRiesgoRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceRiesgoRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceRiesgoRow> e)
    {
        e.ToTable("trade_agreement_service_riesgos");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
        e.Property(x => x.Text).HasColumnType("text");
        e.HasIndex(x => x.ServiceItemId);
    }
}

public sealed class TradeAgreementServiceDependenciaRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceDependenciaRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceDependenciaRow> e)
    {
        e.ToTable("trade_agreement_service_dependencias");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
        e.Property(x => x.Text).HasColumnType("text");
        e.HasIndex(x => x.ServiceItemId);
    }
}

public sealed class TradeAgreementServiceTerminacionCausaRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceTerminacionCausaRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceTerminacionCausaRow> e)
    {
        e.ToTable("trade_agreement_service_terminacion_causas");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
        e.Property(x => x.Text).HasColumnType("text");
        e.HasIndex(x => x.ServiceItemId);
    }
}

public sealed class TradeAgreementServiceMonedaRowConfiguration : IEntityTypeConfiguration<TradeAgreementServiceMonedaRow>
{
    public void Configure(EntityTypeBuilder<TradeAgreementServiceMonedaRow> e)
    {
        e.ToTable("trade_agreement_service_monedas");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(64);
        e.Property(x => x.ServiceItemId).HasMaxLength(80);
        e.Property(x => x.Code).HasMaxLength(16);
        e.HasIndex(x => x.ServiceItemId);
    }
}
