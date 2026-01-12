using Discord;

namespace sblngavnav5X.Commands;

public class HelpCommands
{
        public static List<Embed> GetAllHelpPages()
        {
            var pages = new List<Embed>
            {
                BuildMainHelp(),
                BuildMusicHelp(),
                BuildFunHelp(),
                BuildVipHelp()
            };
            return pages;
        }

        private static Embed BuildMainHelp()
        {
            var botzname = new EmbedAuthorBuilder()
                .WithName("Zдарова я сын гавна и это мои основные команды")
                .WithIconUrl("https://assets.coingecko.com/coins/images/8758/large/ShitCoin.png?1561601773");
            var copy = new EmbedFooterBuilder()
                .WithText("стр. 1 \ndev by lucz@lois.media🏃")
                .WithIconUrl("https://upload.wikimedia.org/wikipedia/commons/thumb/b/b0/Copyright.svg/1200px-Copyright.svg.png");

            var q = new EmbedFieldBuilder().WithName("инфа").WithValue("инфа о сервере");
            var w = new EmbedFieldBuilder().WithName("пинг").WithValue("пинг бота к серверу");
            var e = new EmbedFieldBuilder().WithName("позови").WithValue("позвать любого кентика");
            var r = new EmbedFieldBuilder().WithName("эхо").WithValue("дублирует сообщение в войс");
            var t = new EmbedFieldBuilder().WithName("ролл").WithValue("рандом число в диапазоне");
            var y = new EmbedFieldBuilder().WithName("погода").WithValue("погода в горАде");
            var u = new EmbedFieldBuilder().WithName("монетки|мон").WithValue("стоимость популярной крипты");
            var i = new EmbedFieldBuilder().WithName("курс").WithValue("курс рубля к доллару/евро/тенге");
            var o = new EmbedFieldBuilder().WithName("напомни").WithValue("напоминалка в ЛС");
            var p = new EmbedFieldBuilder().WithName("выбери").WithValue("выбор из нескольких вариантов");
            var a = new EmbedFieldBuilder().WithName("памаги").WithValue("команда для помощи (тут тут щас находишься)");
            var s = new EmbedFieldBuilder().WithName("почта @ник").WithValue("отправить челиксу в лс сообщеньку");
            var k = new EmbedFieldBuilder().WithName("клуб").WithValue("модуль книжного клуба");
            var x = new EmbedFieldBuilder().WithName("паста").WithValue("рандомная паста из карбонары");

            var embed = new EmbedBuilder()
                .WithAuthor(botzname)
                .WithFooter(copy)
                .WithColor(Color.DarkBlue)
                .AddField(q)
                .AddField(w)
                .AddField(e)
                .AddField(r)
                .AddField(t)
                .AddField(y)
                .AddField(u)
                .AddField(i)
                .AddField(o)
                .AddField(p)
                .AddField(a)
                .AddField(s)
                .AddField(k)
                .AddField(x);

            return embed.Build();
        }

        private static Embed BuildMusicHelp()
        {
            var botzname = new EmbedAuthorBuilder()
                .WithName("Музыкальные команды AudioSeven")
                .WithIconUrl("https://assets.coingecko.com/coins/images/8758/large/ShitCoin.png?1561601773");
            var copy = new EmbedFooterBuilder()
                .WithText("стр. 2 \ndev by lucz@lois.media🏃")
                .WithIconUrl("https://upload.wikimedia.org/wikipedia/commons/thumb/b/b0/Copyright.svg/1200px-Copyright.svg.png");

            var m1 = new EmbedFieldBuilder().WithName("играй|и").WithValue("играть песенку");
            var m2 = new EmbedFieldBuilder().WithName("выйди|л").WithValue("лив с канала");
            var m3 = new EmbedFieldBuilder().WithName("плейлист|лист").WithValue("очередь композиций");
            var m4 = new EmbedFieldBuilder().WithName("скип|ск").WithValue("скипнуть трек");
            var m5 = new EmbedFieldBuilder().WithName("останови|стоп").WithValue("остановить и очистить плейлист");
            var m7 = new EmbedFieldBuilder().WithName("пауза|пз").WithValue("приостановить");
            var m8 = new EmbedFieldBuilder().WithName("продолжи|прод").WithValue("продолжить");
            var m9 = new EmbedFieldBuilder().WithName("басс|бс").WithValue("басс буст");
            var m10 = new EmbedFieldBuilder().WithName("громоксть|гр").WithValue("управление громкостю (0-500)");
            var m11 = new EmbedFieldBuilder().WithName("перейти|пр").WithValue("перейти по таймингу");
            var m12 = new EmbedFieldBuilder().WithName("залупа|луп").WithValue("вкл/выкл повтор");

            var embed = new EmbedBuilder()
                .WithAuthor(botzname)
                .WithFooter(copy)
                .WithColor(Color.DarkBlue)
                .AddField(m1)
                .AddField(m2)
                .AddField(m3)
                .AddField(m4)
                .AddField(m5)
                .AddField(m7)
                .AddField(m8)
                .AddField(m9)
                .AddField(m10)
                .AddField(m11)
                .AddField(m12);

        return embed.Build();
        }

        private static Embed BuildFunHelp()
        {
            var botzname = new EmbedAuthorBuilder()
                .WithName("Команды для фанчика")
                .WithIconUrl("https://assets.coingecko.com/coins/images/8758/large/ShitCoin.png?1561601773");
            var copy = new EmbedFooterBuilder()
                .WithText("стр. 3 \ndev by lucz@lois.media🏃")
                .WithIconUrl("https://upload.wikimedia.org/wikipedia/commons/thumb/b/b0/Copyright.svg/1200px-Copyright.svg.png");

            var r = new EmbedFieldBuilder().WithName("шутка|анек").WithValue("рандомная шутка по категории (1-3)");
            var r2 = new EmbedFieldBuilder().WithName("цитаты|цит").WithValue("рандом цитата");
            var t = new EmbedFieldBuilder().WithName("гэй").WithValue("узнать ориентацию");
            var z1 = new EmbedFieldBuilder().WithName("кот|кит").WithValue("рандом котик");
            var t2 = new EmbedFieldBuilder().WithName("маг7").WithValue("какой ты сегодня максим");
            var t1 = new EmbedFieldBuilder().WithName("гонка").WithValue("гонка смайликов");
            var m1 = new EmbedFieldBuilder().WithName("пососи").WithValue("пососи (команда SHEFFZ)");
            var m2 = new EmbedFieldBuilder().WithName("сапер (1-9)").WithValue("классический сапёр");
            var X2 = new EmbedFieldBuilder().WithName("сетарех|дуэль").WithValue("выдача сета/дуэли в арехе");
            var z = new EmbedFieldBuilder().WithName("волк").WithValue("рандом волк");
            var zxc = new EmbedFieldBuilder().WithName("андерстендебел").WithValue("андерстендебел");
            var zxc1 = new EmbedFieldBuilder().WithName("8 яиц|?").WithValue("аналог шара-восьмерки");

            var embed = new EmbedBuilder()
                .WithAuthor(botzname)
                .WithFooter(copy)
                .WithColor(Color.DarkBlue)
                .AddField(r)
                .AddField(r2)
                .AddField(m2)
                .AddField(X2)
                .AddField(z)
                .AddField(z1)
                .AddField(zxc1)
                .AddField(t2)
                .AddField(t1)
                .AddField(zxc)
                .AddField(t)
                .AddField(m1);

            return embed.Build();
        }

        private static Embed BuildVipHelp()
        {
            var botzname = new EmbedAuthorBuilder()
                .WithName("Мои вип/мод команды😎")
                .WithIconUrl("https://assets.coingecko.com/coins/images/8758/large/ShitCoin.png?1561601773");
            var copy = new EmbedFooterBuilder()
                .WithText("стр. 4 \ndev by lucz@lois.media🏃")
                .WithIconUrl("https://upload.wikimedia.org/wikipedia/commons/thumb/b/b0/Copyright.svg/1200px-Copyright.svg.png");

            var r = new EmbedFieldBuilder().WithName("ир").WithValue("инфа для девелоперов");
            var r2 = new EmbedFieldBuilder().WithName("анонс|ембед").WithValue("анонс или эмбед в любой канал");
            var m1 = new EmbedFieldBuilder().WithName("бан|кик").WithValue("ну итак все понятно, еп");
            var u = new EmbedFieldBuilder().WithName("удоли").WithValue("удалить сообщения <кол-во>");
            var zxc1 = new EmbedFieldBuilder().WithName("версия").WithValue("версия бота");
            var zxc2 = new EmbedFieldBuilder().WithName("апт").WithValue("аптайм бота");
            var zxc3 = new EmbedFieldBuilder().WithName("актив").WithValue("ставит активность бота");
            var t = new EmbedFieldBuilder().WithName("ст").WithValue("меняет статус бота");

            var embed = new EmbedBuilder()
                .WithAuthor(botzname)
                .WithFooter(copy)
                .WithColor(Color.DarkBlue)
                .AddField(r)
                .AddField(r2)
                .AddField(m1)
                .AddField(u)
                .AddField(zxc1)
                .AddField(zxc2)
                .AddField(zxc3)
                .AddField(t);

            return embed.Build();
        }
}