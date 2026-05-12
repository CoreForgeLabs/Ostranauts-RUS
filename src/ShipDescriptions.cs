using System.Collections.Generic;

namespace OstranautsRusPatch
{
    /// <summary>
    /// Ship description translations for the Russian localization.
    /// Each key is the exact English description from the ship JSON files (after JSON unescaping).
    /// Values are the corresponding Russian translations.
    /// </summary>
    public static class ShipDescriptionTranslations
    {
        public static Dictionary<string, string> GetAll()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();

            // --- Ryokka CR-43 ---
            d["Built during the colony rush, this enormous reactor-powered freighter has abundant floor space for hauling and two cabins that each comfortably sleep two. Given that the ship was designed to construct and expand structures throughout the System, it is ideal for long-duration travel and hauling."] =
                "Построен в эпоху колониальной гонки. Этот огромный грузовоз с реакторным приводом располагает обширной грузовой площадью и двумя каютами, каждая из которых с комфортом вмещает двоих. Поскольку корабль проектировался для строительства и расширения объектов по всей Системе, он идеален для длительных перелётов и перевозки грузов.";

            // --- Ryokka CR-43 Indie Retrofit ---
            d["An independent operator refit of the Ryokka CR-43, a colony rush mainstay. This variant sacrifices one sleeping cabin for a medbay, and some cargo area for a rec room. Designed for hauling in comfort over long periods."] =
                "Независимая переделка Ryokka CR-43 — рабочей лошадки колониальной гонки. В этой модификации одна спальная каюта заменена на медотсек, а часть грузового пространства — на комнату отдыха. Создан для комфортных перевозок на длительных маршрутах.";

            // --- Mobile Space Systems Cobra ---
            d["An aero racing chassis built in partnership with Mobile Space Systems. The Cobra serves as the platform for many competing teams' entries into aero racing leagues across the System."] =
                "Атмосферное гоночное шасси, созданное совместно с Mobile Space Systems. Cobra служит платформой для множества команд, участвующих в аэрогоночных лигах по всей Системе.";

            // --- MSS Argute ---
            d["An extremely fast and highly nimble ship boasting a Miura intake regulator and two packages of ten thrusters on each wing. Originally built as a show model to demonstrate the strength of MSS ship frames, this rare build found a niche as a short range courier for time-critical, small-volume deliveries in torch-limited airspace."] =
                "Чрезвычайно быстрый и манёвренный корабль с регулятором забора Miura и двумя блоками по десять двигателей на каждом крыле. Изначально построен как демонстрационный образец для показа прочности корпусов MSS, но этот редкий аппарат нашёл свою нишу в качестве ближнего курьера для срочных малогабаритных доставок в зонах с ограничением маршевого хода.";

            // --- Van Hummel Boomerang ---
            d["This unusual airframe is one of Van Buren's most recent entries into the Venusian market. Its asymmetric design has proven to be highly divisive among consumers, which has only added to its demand from customers wanting to make a bold statement."] =
                "Этот необычный летательный аппарат — одна из последних разработок Van Hummel для венерианского рынка. Асимметричный дизайн вызвал жаркие споры среди покупателей, что лишь подогрело спрос со стороны тех, кто хочет выделиться.";

            // --- Testudo Bulk Lifter Mk. III ---
            d["A medium sized, reactor powered craft that houses 2. The notable features of this craft are its two large externally ventable/accessible cargo areas that the rest of the ship is bolted onto and around. This unique cargo arrangement allows for variable configurations outside that of standardised cargo pod craft."] =
                "Среднеразмерное реакторное судно на два человека. Главная особенность — два больших грузовых отсека с внешним вентилированием и доступом, вокруг которых собран весь остальной корабль. Эта уникальная компоновка позволяет использовать нестандартные конфигурации загрузки, недоступные обычным контейнеровозам.";

            // --- Custom Coffin ---
            d["A custom racer scratch built for illegal RCS circuit rallies. Maximized for speed and maneuverability, so all systems not relevant to these ends have been cut, including life-support. About as safe as the name implies."] =
                "Самодельный гоночный аппарат, собранный для нелегальных кольцевых гонок на маневровых двигателях. Оптимизирован исключительно под скорость и манёвренность — все прочие системы срезаны, включая жизнеобеспечение. Безопасность примерно на уровне, который подразумевает название.";

            // --- Ryokka CR-53b ---
            d["An enormous reactor-powered freighter with abundant floor space. Built as an upgrade to the CR-43, the 53b was Ryokka's first collaboration with Mobile Space Systems, and features a lighter weight frame than its predecessor. Ideal for long-haul travel."] =
                "Огромный реакторный грузовоз с обширным полезным пространством. Созданный как модернизация CR-43, модель 53b стала первой совместной разработкой Ryokka и Mobile Space Systems и отличается более лёгким каркасом по сравнению с предшественником. Идеален для дальних перевозок.";

            // --- Testudo Edelweiss ---
            d["A large reactor powered freighter with zero frills. Some claim the Edelweiss was a cheaply thrown together imitation of the Ryokka CR-43, built to cash in on the colony rush and pushed into production as soon as Testudo's executives got their hands on Ryokka's early design concepts."] =
                "Большой реакторный грузовоз без каких-либо излишеств. Поговаривают, что Edelweiss — наспех собранная имитация Ryokka CR-43, запущенная в производство, едва руководство Testudo заполучило ранние концепты Ryokka, ради наживы на колониальной гонке.";

            // --- Testudo Halberd ---
            d["A reactor powered pleasure craft with a single cabin and twin linked Miura \"Hydra\" intake regulators. Built as a collaboration with Mobile Space Systems, half the ship has luxury \"orange way\" interiors, making the Halberd a highly sought after ship for solo spacers."] =
                "Реакторное прогулочное судно с одной каютой и сдвоенными регуляторами забора Miura \"Hydra\". Построено совместно с Mobile Space Systems — половина корабля отделана в роскошном стиле \"orange way\", что делает Halberd весьма желанным судном среди одиночных космонавтов.";

            // --- Custom Hand of God ---
            d["It is said that god cannot kill what god cannot catch. Something upon which this shipwright was clearly willing to stake their life."] =
                "Говорят, бог не может убить то, что бог не может поймать. Судя по всему, строитель этого корабля был готов поставить на это свою жизнь.";

            // --- Ryokka TU-44a Heavy Tug ---
            d["A reactor powered cabinless tug with a low end RCS package. Despite being an incredibly efficient design, the TU44-a is regarded as a bit of an ugly duckling given its non-standard layout."] =
                "Реакторный бескабинный буксир с базовым набором маневровых двигателей. Несмотря на невероятно эффективную конструкцию, TU-44a считается своего рода «гадким утёнком» из-за нестандартной компоновки.";

            // --- Testudo Ibex ---
            d["This reactor powered long range survey and exploration vessel can house a small crew in relative comfort. Includes two adjacent air tight bays rated for science and engineering. Given the potentially volatile nature of the experiments done on the Ibex, the ship has additional redundancy in the form of two Nav stations in the standard design."] =
                "Реакторное исследовательское судно дальнего радиуса действия, способное разместить небольшой экипаж в относительном комфорте. Оснащено двумя смежными герметичными отсеками для научных и инженерных работ. Учитывая потенциально опасный характер экспериментов на борту Ibex, стандартная комплектация включает два навигационных поста для дополнительной надёжности.";

            // --- Testudo Class 14 Inspection Capsule ---
            d["A small craft designed for close inspection work of stations and larger ships. Seats one, and not comfortably."] =
                "Небольшой аппарат для инспекционных работ вблизи станций и крупных кораблей. Вмещает одного — и не сказать, чтобы удобно.";

            // --- Testudo Katydid ---
            d["One of the smallest possible spacecraft with an airlock and environmental system, the Katydid is built with MSS parts to cut down even further on weight. The final design was made even smaller when a clever engineer realized that the life support and RCS intake regulator could share a single O2 canister."] =
                "Один из минимально возможных космических аппаратов, оснащённый шлюзом и системой жизнеобеспечения. Katydid построен с комплектующими MSS для максимального снижения массы. Итоговая конструкция стала ещё компактнее, когда находчивый инженер обнаружил, что жизнеобеспечение и регулятор забора маневровой системы могут использовать общий кислородный баллон.";

            // --- Ryokka LB-77a ---
            d["Nicknamed the \"cattle car,\" the LB-77a is designed to ferry EVA suited workers to job sites in local space as cheaply as possible. In the OKLG Boneyard it's not unusual to see dozens of spacers packed onto the vessel, clinging to cargo webbing, half of them mooing as a joke."] =
                "Прозванный \"скотовозом\", LB-77a спроектирован для перевозки рабочих в скафандрах к объектам в местном пространстве максимально дёшево. На Кладбище OKLG нередко можно увидеть десятки космонавтов, набитых в это судно и цепляющихся за грузовые сетки, — причём половина мычит ради шутки.";

            // --- Renske International Li Bai Gen II ---
            d["One of the largest mass-manufactured luxury pleasure yachts, the Li Bai was built by Van Hummel under contract from the hospitality corporation Renske International. The ship is used primarily by the uber-wealthy in the Martian system, especially those being ferried between the planet and its orbitals. Divided starkly between crew and client quarters, the ship sleeps nine comfortably and comes standard with a full cherrywood bar."] =
                "Одна из крупнейших серийных прогулочных яхт класса люкс. Li Bai построена компанией Van Hummel по контракту с гостиничной корпорацией Renske International. Яхта используется преимущественно сверхбогатыми людьми в марсианской системе, особенно для перелётов между планетой и орбитальными станциями. Корабль чётко разделён на зоны экипажа и клиентов, с комфортом вмещает девять человек и оснащён полноценной барной стойкой из вишнёвого дерева.";

            // --- Ryokka TX-82d Light Tug ---
            d["A small salvage ship designed for pushing ships and cargo pods around in local space. Comes standard with a tow brace. Packed with additional RCS thrusters and a spiral of intake regulators to create greater thrust than other similar sized tugs."] =
                "Малый буксир для перемещения кораблей и грузовых контейнеров в местном пространстве. В стандартной комплектации — буксировочная штанга. Оснащён дополнительными маневровыми двигателями и спиральной системой регуляторов забора для создания большей тяги, чем у аналогов сопоставимых размеров.";

            // --- Testudo Ferry Mk. IX ---
            d["A small passenger ferry that seats half a dozen and is used for short duration travel. Lacks major amenities beyond barebones environmental management. Includes a toilet but you probably don't want to use it..."] =
                "Небольшой пассажирский паром на полдюжины мест, используемый для коротких перелётов. Не предлагает комфорта помимо базового жизнеобеспечения. Туалет имеется — но вы вряд ли захотите им воспользоваться...";

            // --- Testudo Charon Mk. I ---
            d["An extremely large, reactor-powered passenger ferry that seats over a dozen.  Designed for medium range travel given the lack of cabins, but is often retrofitted for longer hauls. A spacer superstition dictates that passengers are to tip two yuan to a Charon pilot after a successful journey."] =
                "Чрезвычайно большой реакторный пассажирский паром, вмещающий более дюжины пассажиров. Спроектирован для маршрутов средней дальности ввиду отсутствия кают, но часто переоборудуется для более длинных рейсов. По космонавтскому суеверию, пассажиры должны давать два юаня чаевых пилоту Charon после успешного перелёта.";

            // --- Testudo Mesa Mk. I ---
            d["Perhaps the most iconic freighter in the early Testudo line, the Mesa is essentially eight big boxes and a long corridor. The initial production run began in 2041 but lasted well into the late 2050s. While not produced in the same volume as the Edelweiss due to issues with assembly facilities, the Mesa is widely regarded as the first true freighter of the colony rush."] =
                "Пожалуй, самый культовый грузовоз ранней линейки Testudo — Mesa по сути представляет собой восемь больших ящиков и длинный коридор. Первоначальный выпуск начался в 2041 году и продолжался до конца 2050-х. Хотя Mesa производилась не столь массово, как Edelweiss, из-за проблем со сборочными мощностями, она повсеместно считается первым настоящим грузовозом колониальной гонки.";

            // --- Testudo Myna Mk. I ---
            d["A reactor-powered, cabinless courier with a minimal RCS package. Originally developed as an extended-range military interceptor, the Myna's weapons were stripped after the contract fell through late into negotiations. The housings left behind from the weapons were unceremoniously converted into a bathroom."] =
                "Реакторный бескабинный курьер с минимальным набором маневровых двигателей. Изначально разрабатывался как дальний военный перехватчик, но оружие было демонтировано после срыва контракта на поздней стадии переговоров. Оставшиеся от вооружения отсеки были бесцеремонно переделаны в санузел.";

            // --- Testudo Ocelot ---
            d["A large, cylindrical, reactor-powered freighter that comes standard with two D2O tanks. The Ocelot gained minor notoriety in 2078 when an unskilled pilot dragged one across the surface of 1036 Ganymed after miscalculating a simple maneuver. Now folks say the ship only has eight lives left."] =
                "Большой цилиндрический реакторный грузовоз, укомплектованный двумя баками тяжёлой воды (D2O). Ocelot приобрёл некоторую известность в 2078 году, когда неопытный пилот протащил его по поверхности 1036 Ганимед, ошибившись в простом манёвре. Теперь говорят, что у корабля осталось только восемь жизней.";

            // --- Van Hummel Pequod ---
            d["One of the only luxury freighters in existence, the Pequod is used primarily as a showfloor for the kind of uber-wealthy client who insists on seeing their cargo loaded and unloaded personally. Reactor powered, sleeps two with room for more if the cargo bays are retrofitted for crew."] =
                "Один из немногих существующих грузовозов класса люкс. Pequod используется преимущественно как выставочный зал для сверхбогатых клиентов, настаивающих на личном присутствии при погрузке и разгрузке. Оснащён реактором, вмещает двоих с возможностью расширения, если грузовые отсеки переоборудовать под каюты.";

            // --- Farrow Primigenial ---
            d["An extremely fast, lightweight RCS ship with a design and interior layout inspired by Earther sailing vessels. Boasts double Miura Hydra Intake Regulators and an RCS package with two dozen thrusters. A favorite among the elite Martian hobbyist class in West Lake, the Primigenial is nicknamed the \"Hangzhou Hangman\" because of how many amateur pilots it kills every year."] =
                "Чрезвычайно быстрый и лёгкий маневровый корабль, дизайн и планировка которого вдохновлены земными парусниками. Оснащён сдвоенными регуляторами забора Miura Hydra и пакетом из двух дюжин маневровых двигателей. Любимец марсианской элиты из Вест-Лейк. Primigenial получил прозвище \"Ханчжоуский палач\" из-за количества пилотов-любителей, которых он убивает ежегодно.";

            // --- Ryokka RF-72m ---
            d["A product of executive meddling. The RF-72m was initially designed as a large, longer duration construction vessel, able to house work crews for weeks on end. The introduction of the Testudo Mesa during its development pushed Ryokka into contracting Mobile Space Systems to upgrade its cargo capacity with the addition of four large, flimsy external cargo units to market the ship as a freighter. The move was a costly failure, producing a delicate, overpriced and resource-hungry vessel."] =
                "Продукт вмешательства руководства. RF-72m изначально проектировался как большое строительное судно длительного пребывания, способное размещать рабочие бригады неделями. Появление Testudo Mesa в ходе разработки вынудило Ryokka привлечь Mobile Space Systems для наращивания грузоёмкости путём добавления четырёх больших, но хлипких внешних грузовых модулей, чтобы продвигать корабль как грузовоз. Решение оказалось провальным — получилось хрупкое, чрезмерно дорогое и ресурсоёмкое судно.";

            // --- Testudo Rouncy ---
            d["A smallish reactor-powered all purpose vessel perfect for a solo spacer. Comes standard with two Miura \"Hydra\" Intake Regulators. One of Testudo's most popular designs. Often compared favorably to the Myna, another Testudo design."] =
                "Компактное реакторное многоцелевое судно, идеальное для одиночного космонавта. В стандартной комплектации — два регулятора забора Miura \"Hydra\". Один из самых популярных проектов Testudo. Часто выгодно сравнивается с Myna — ещё одной разработкой Testudo.";

            // --- Van Hummel Royal Flush ---
            d["One of the rare small ships manufactured by luxury builder Van Hummel, the Royal Flush is used to shuttle VIP passengers between one expensive location and another. The ship lacks a reactor but is extremely nimble on RCS, boasting two wings with six thrusters each and a Miura intake regulator."] =
                "Один из редких малых кораблей от люксового производителя Van Hummel. Royal Flush используется для перевозки VIP-пассажиров между одной дорогой локацией и другой. Лишён реактора, но чрезвычайно манёвренен на маневровых двигателях — два крыла по шесть двигателей на каждом и регулятор забора Miura.";

            // --- Custom Sled ---
            d["A hodgepodge of smuggled parts hastily welded together and exposed to hard vacuum. Nearly impossible to pilot without an EVA suit. A ship only a mother could love."] =
                "Мешанина из контрабандных деталей, наскоро сваренных воедино и открытых жёсткому вакууму. Пилотировать без скафандра практически невозможно. Корабль, который только мать может полюбить.";

            // --- Custom Box Sled ---
            d["A hodgepodge of smuggled parts hastily welded together with only a single room equipped for safe travel. The exposed center deck between the airlock and cabin make piloting this ship without a pressure suit nearly impossible. Hard to even call a fixer-upper."] =
                "Мешанина из контрабандных деталей, наскоро сваренных воедино, с единственным помещением, пригодным для безопасного перемещения. Открытая центральная палуба между шлюзом и кабиной делает пилотирование без скафандра практически невозможным. Даже «требует ремонта» — слишком мягкая характеристика.";

            // --- Ryokka TU-77a Salvage Pod ---
            d["One of a series of small salvage ships built by Ryokka on contract with Ayotimiwa. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров-утилизаторов, построенных Ryokka по контракту с Ayotimiwa. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Ryokka TU-76a Endurance ---
            d["A salvage pod with bed and wash facilities built for longer duration salvage jobs. Had limited success given that the TU-77a was released shortly after this design. Comes standard with four large batteries."] =
                "Утилизационная капсула с койкой и санузлом, построенная для длительных рейсов. Имела ограниченный успех, поскольку модель TU-77a была выпущена вскоре после неё. В стандартной комплектации — четыре больших аккумулятора.";

            // --- Ryokka TU-70a Small Salvage Pod ---
            d["One of a series of small tugs built by Ryokka on contract with Ayotimiwa. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров, построенных Ryokka по контракту с Ayotimiwa. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Testudo Lilliput ---
            d["A relic from 2038, the Lilliput was built by Testudo before the colony rush as a ferry between the Earth-side space elevators and Luna. It would look more at home in a museum than in flight. It's very possible this ship was put into consumer circulation by mistake."] =
                "Реликвия 2038 года. Lilliput был построен Testudo до колониальной гонки как паром между земными космическими лифтами и Луной. Больше подошёл бы для музея, чем для полётов. Весьма вероятно, что этот корабль попал в свободную продажу по ошибке.";

            // --- Custom Retrofit (SCI Retrofit) ---
            d["A standard cargo container retrofitted with the bare minimum kit necessary to navigate from one point in space to another. An EVA suit is recommended in the event the origin or destination lacks air, or the trip is longer than 15 minutes. Basically a death trap. But cheap!"] =
                "Стандартный грузовой контейнер, дооснащённый минимально необходимым набором для навигации из точки А в точку Б. Скафандр рекомендуется на случай, если начальный или конечный пункт лишены воздуха, или поездка длится более 15 минут. По сути — ловушка для смертников. Зато дёшево!";

            // --- Testudo 4C Intermodal ---
            d["A Testudo-manufactured, standard-sized shipping container often stacked on super large cargo liners. Intermodals are often shunted off by small tugs on ballistic trajectories across The System, with the expectation of being caught at their destination by a second tugboat. It means a single small set of tugs can move thousands of tonnes more cargo than a freighter, but the delivery time is in the region of weeks to months rather than hours to days."] =
                "Стандартный грузовой контейнер производства Testudo, обычно устанавливаемый на сверхкрупные грузовые лайнеры. Интермодалы нередко отправляются малыми буксирами по баллистическим траекториям через Систему с расчётом на перехват вторым буксиром в пункте назначения. Благодаря этому небольшая группа буксиров может перемещать на тысячи тонн больше груза, чем грузовоз, но доставка занимает от нескольких недель до месяцев вместо часов или дней.";

            // --- Testudo Dream ---
            d["A small shuttlecraft designed for ferrying between structures and ships in local space. Built to accommodate either a small cabin, quad jump seats, or cargo space, depending on the model."] =
                "Малый шаттл для перевозок между конструкциями и кораблями в местном пространстве. В зависимости от модификации может быть оснащён небольшой каютой, четырьмя откидными сиденьями или грузовым пространством.";

            // --- Testudo Squall ---
            d["A medium-sized shuttle developed by Testudo for limited operations around the Aerostats of Venus."] =
                "Среднеразмерный шаттл, разработанный Testudo для ограниченных операций вблизи аэростатов Венеры.";

            // --- Testudo Sundancer XR ---
            d["Based on the Sundancer platform, the XR edition includes an RCS maneuvering system to enable limited travel outside the atmosphere."] =
                "Создан на базе платформы Sundancer. Версия XR включает маневровую двигательную установку для ограниченных полётов за пределами атмосферы.";

            // --- Testudo Sundancer ---
            d["Little more than an enclosed pilot seat and some rotors, this commuter craft represents the barest minimum of air transport. Its size, appearance, and relative ubiquity has earned it the more common moniker of \"Mosquito\" among locals."] =
                "Немногим больше закрытого пилотского кресла и нескольких роторов — этот коммутер представляет собой абсолютный минимум воздушного транспорта. Размер, внешний вид и относительная повсеместность принесли ему среди местных более распространённое прозвище \"Москит\".";

            // --- Ryokka Tanker TU-66a (D2O) ---
            d["One of a series of small tugs built by Ryokka on contract with Ayotimiwa, the 66a hauls deuterium. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров Ryokka по контракту с Ayotimiwa; модель 66a перевозит дейтерий. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Ryokka Tanker TU-65a (He-3) ---
            d["One of a series of small tugs built by Ryokka on contract with Ayotimiwa, the 65a hauls Helium-3. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров Ryokka по контракту с Ayotimiwa; модель 65a перевозит гелий-3. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Ryokka Tanker TU-7a (N2) ---
            d["One of a series of small tugs built by Ryokka on contract with Ayotimiwa, the 7a hauls Nitrogen. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров Ryokka по контракту с Ayotimiwa; модель 7a перевозит азот. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Ryokka Tanker TU-8a (O2) ---
            d["One of a series of small tugs built by Ryokka on contract with Ayotimiwa, the 8a hauls Oxygen. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров Ryokka по контракту с Ayotimiwa; модель 8a перевозит кислород. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Ryokka Tanker TU-1a (Unmarked) ---
            d["One of a series of small tugs built by Ryokka on contract with Ayotimiwa, the 1a is not specialised for any one fluid. Vessels in the Ryokka TU line are ubiquitous and sought after in the OKLG Boneyard given their efficiency for solo spacers and relatively favorable mortgage rates. It's rumored that the corporation has dozens more factory fresh TU models mothballed in a secret hangar as a strategy to create artificial scarcity."] =
                "Один из серии малых буксиров Ryokka по контракту с Ayotimiwa; модель 1a не специализируется ни на одном конкретном типе жидкости. Суда линейки Ryokka TU повсеместно распространены и востребованы на Кладбище OKLG благодаря эффективности для одиночных космонавтов и относительно выгодным ипотечным условиям. Ходят слухи, что корпорация хранит десятки заводских моделей TU на секретном складе в рамках стратегии искусственного дефицита.";

            // --- Testudo Tombolo Mk. II ---
            d["Despite being manufactured in 2046, the Tombolo Mk. II freighter remains one of the most popular ships in the System. Powered by a fusion reactor and outfitted with four independently pressurized cargo bays with optional external webbing, the Tombolo is about the largest ship an experienced captain could hope to operate comfortably while flying solo."] =
                "Несмотря на год выпуска — 2046, грузовоз Tombolo Mk. II остаётся одним из самых популярных кораблей в Системе. Оснащённый реактором синтеза и четырьмя независимо герметизированными грузовыми отсеками с опциональной внешней обвязкой, Tombolo — примерно крупнейший корабль, которым опытный капитан может комфортно управлять в одиночку.";

            // --- Testudo Tricorn Mk. II ---
            d["A small, personal craft designed for atmospheric and limited SSTO operations. The unconventional third rotor gives it a surprising amount of power, and uses specially-tuned timings to counterbalance against angular momentum."] =
                "Небольшой личный аппарат для атмосферных и ограниченных одноступенчатых орбитальных полётов. Нестандартный третий ротор придаёт ему удивительную мощность и использует специально настроенные режимы для компенсации углового момента.";

            // --- Testudo Melody ---
            d["The Testudo Melody shares some similarities with the Ryokka TU-77a salvage pod, but with batteries, canisters, intake regulators and RCS thrusters all on the outside. It's a more capable ship than the ubiquitous Ryokka model, but more difficult to maintain, especially without an EVA suit."] =
                "Testudo Melody имеет сходство с утилизационной капсулой Ryokka TU-77a, но аккумуляторы, баллоны, регуляторы забора и маневровые двигатели расположены снаружи. Это более многофункциональный корабль, чем массовая модель Ryokka, но он сложнее в обслуживании, особенно без скафандра.";

            // --- MSS Vector Mk. II ---
            d["A standard law enforcement cruiser. Sluggish but durable. Comes standard with a coffee machine, but no donuts."] =
                "Стандартный патрульный крейсер правоохранительных органов. Неповоротлив, но вынослив. В стандартной комплектации — кофемашина, пончики не прилагаются.";

            // --- MSS Vector Mk. III ---
            d["A high speed variant of the standard law enforcement vessel, designed to address complaints from law enforcement personnel about the Mark II's limited ability to steamroll targets."] =
                "Скоростная модификация стандартного патрульного судна, разработанная в ответ на жалобы сотрудников правоохранительных органов на ограниченную способность Mark II настигать цели.";

            // --- Custom Volatile (Aero) ---
            d["A surprisingly common retrofit of a popular space hot rod, adapted for atmospheric flight."] =
                "Удивительно распространённая переделка популярного космического хот-рода, адаптированная для атмосферных полётов.";

            // --- Custom Volatile (Prize Ship) ---
            d["Hot rod RCS racer with a huge bank of external thruster packages. Capable of extremely high speeds, outrunning even intelligence."] =
                "Хот-род на маневровых двигателях с массивным блоком внешних двигательных пакетов. Способен развивать чрезвычайно высокие скорости, обгоняя даже разум.";

            // --- Custom Whistler ---
            d["A custom, high-end RCS racer with coveted \"orange way\" interiors. Not much room, but plenty of vroom."] =
                "Кастомный маневровый гоночный аппарат высшего класса с вожделенной отделкой \"orange way\". Места немного, зато мощности — хоть отбавляй.";

            // --- Ostrich Aero 4R ---
            d["The Ostrich is a cargo ship with 4 cargo pods, designed for efficient atmospheric transport"] =
                "Ostrich — грузовой корабль с 4 грузовыми контейнерами, спроектированный для эффективных атмосферных перевозок.";

            // --- Ostrich Aero 8R ---
            d["The Ostrich is a cargo ship with 8 cargo pods, designed for efficient atmospheric transport"] =
                "Ostrich — грузовой корабль с 8 грузовыми контейнерами, спроектированный для эффективных атмосферных перевозок.";

            return d;
        }
    }
}
