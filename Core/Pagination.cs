using Discord;

namespace sblngavnav5X.Core
{
    public static class Pagination
    {
        public static int TotalPages(int count, int pageSize)
            => Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));

        public static ComponentBuilder AddPager(this ComponentBuilder builder, int page, int totalPages, string idPrefix, int row)
        {
            page = Math.Clamp(page, 0, totalPages - 1);

            builder.WithButton("◀️", $"{idPrefix}:{Math.Max(page - 1, 0)}", ButtonStyle.Secondary, disabled: page == 0, row: row);
            builder.WithButton($"Стр. {page + 1}/{totalPages}", $"{idPrefix}:noop", ButtonStyle.Secondary, disabled: true, row: row);
            builder.WithButton("▶️", $"{idPrefix}:{Math.Min(page + 1, totalPages - 1)}", ButtonStyle.Secondary, disabled: page >= totalPages - 1, row: row);

            return builder;
        }
    }
}
