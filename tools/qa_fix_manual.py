# -*- coding: utf-8 -*-
"""
Хвост находок, который не поддался ни регуляркам, ни моделям: правки написаны
вручную, каждая сверена со своим английским оригиналом.

Три семейства:

1. Остатки переведённых токенов ([говорит] вместо [says]). Автоматика их не
   взяла, потому что в этих строках число выдуманных токенов не совпадало с
   числом потерянных, и позиционное выравнивание было бы гаданием.

2. "[them] [was] преступником" из "[them] [is] a criminal". Связка [is] в
   русском настоящем времени пустая, поэтому нужен ИМЕНИТЕЛЬНЫЙ падеж
   ("[them] [is] преступник" -> "Он преступник"), а не творительный: последний
   требует явной связки "был"/"является", которой здесь нет. Прошедшее время
   [was] тут просто неверно -- в оригинале настоящее.

3. Глагол, вписанный текстом, и застывшее "собирается".

  python tools/qa_fix_manual.py [--apply]
"""
import json, io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DIR = os.path.join(ROOT, "langs", "ru", "data")
LF = chr(10)

# Токены, переведённые вместе с текстом. Сверено с оригиналом каждой строки.
TOKEN_MAP = {
    "[говорит]": "[says]",
    "[поддерживает]": "[is]",
    "[одаривает]": "[flashes]",
    "[показывает]": "[shows]",
    "[отстыковывается]": "[undocks]",
    "[садится]": "[steps]",
    "[устремляется]": "[darts]",
}

# Замены с указанием поля: (id, поле) -> новый шаблон. У SOCFlirtStart* русские
# strDesc и strTooltip стояли крест-накрест: в описании лежал перевод подсказки,
# и наоборот. Поэтому правка только strDesc делала хуже -- нужно оба поля.
REWRITE_FIELD = {
    ("SOCFlirtStartWoman", "strDesc"):
        "[us] [checks], испытывает ли [us-contractIs] влечение к [them-dat].",
    ("SOCFlirtStartWoman", "strTooltip"):
        "[us] многозначительно [raises] брови, глядя на [them-acc].",
    ("SOCFlirtStartNB", "strDesc"):
        "[us] [checks], испытывает ли [us-contractIs] влечение к [them-dat].",
    ("SOCFlirtStartNB", "strTooltip"):
        "[us] многозначительно [raises] брови, глядя на [them-acc].",
}

# Полные замены строк: id -> новый шаблон.
REWRITE = {
    # Связка настоящего времени: именительный падеж, а не творительный.
    "Plot_WhodunnitBasicsAge01": "[them] [is] ребёнок.",
    "Plot_WhodunnitBasicsAge02": "[them] [is] молодой человек.",
    "Plot_Whodunnit_ExamineForFactionReplyCriminal":
        "[them] [is] преступник. Сообщение кому-то влиятельному в криминальном мире "
        "или офицеру AyoSec, скорее всего, принесёт благосклонность или награду от "
        "соответствующей стороны.",
    "Plot_Whodunnit_ExamineForFactionReplyManager":
        "[them] [is] сотрудник корпорации Ayotimiwa. Сообщение AyoSec или менеджеру "
        "корпорации Ayotimiwa может принести благосклонность или награду.",
    "Plot_Whodunnit_ExamineForFactionReplyPirate":
        "[them] [is] изгой, которого уважают немногие, но чью смерть многие бы "
        "отпраздновали. Если вы найдёте кого-то, кто считает [them-acc] врагом, вы "
        "можете обрести друга. В противном случае местные сотрудники службы "
        "безопасности всегда рады сообщить об очередной угрозе, которая больше не "
        "представляет опасности для их юрисдикции.",
    "Plot_Whodunnit_ExamineForFactionReplyBartender":
        "[them] [is] бармен, а значит, [them-subj], скорее всего, [was] хорошо "
        "известен[them-endsadj] и имел[them-ends] обширные связи. Сообщение другому "
        "бармену или офицеру AyoSec может принести благосклонность или награду.",
    # Глагол возвращается в токен.
    "SOCWriteHome":
        "[us] [pauses], чтобы добавить строчку в длинное письмо домой на своём КПК.",
    "SOCOfferThank":
        "[us] [thanks] [them-acc] и с радостью [us-accepts] предложение.",

    # Застывшее "собирается" при пустой связке.
    "PLGAIReportCrimeGalConTrespassNPCDone":
        "[us] больше не [is.aux] сообщать о преступлении.",
    "PLGAIReportCrimeSVIRTrespassNPCDone":
        "[us] больше не [is.aux] сообщать о преступлении.",
    # "[us] [has] been used by player" -- страдательное настоящее, не прошедшее.
    "IsNavStationUsed":
        "[us] [has] использован[us-endsadj] игроком.",
    "Plot_Whodunnit_ExamineForFactionReplyShipbreaker":
        "[them] [is] разборщик кораблей — самая распространённая душа на Кладбище. "
        "Сообщить другу или ближайшему родственнику было бы добрым поступком, но "
        "сообщение AyoSec или другому разборщику может принести благосклонность или "
        "награду.",
    "Plot_Whodunnit_ExamineForFactionReplyLEO":
        "[them] [is] подрядчик, работающий на частную военную компанию AyoSec. "
        "Сообщение менеджеру корпорации Ayotimiwa или другому офицеру AyoSec может "
        "принести благосклонность или награду.",
    # Тире вместо связки: с токеном [is] (в настоящем он пустой) фраза
    # согласуется, а "умел(а)" и "трезв" получают род вместо скобок.
    "PLOT_Merga_MergaEngaged06_Reply":
        "[us] [says], что [3rd] [is] назойливый маленький урод. К тому же, запись не "
        "доказывает, что [us-subj] [was] трезв[us-endsadj]. [us-contractHas] всегда "
        "умел[us-ends] пить и не пьянеть.",
    "SOCAskFamilyAcceptSiblings3":
        "[us] [tells] [them-dat], что [us-subj] [is] очень близок[us-endsadj] с одним "
        "из своих братьев или сестёр.",
}


def main():
    write = "--apply" in sys.argv
    tok_hits, rew_hits, missed = 0, 0, []
    for name in sorted(os.listdir(RU_DIR)):
        if not name.endswith(".json") or name.endswith("_translated.json"):
            continue
        path = os.path.join(RU_DIR, name)
        data = json.load(io.open(path, encoding="utf-8-sig"))
        touched = 0
        for rec_id, rec in data.items():
            if not isinstance(rec, dict):
                continue
            for field, val in list(rec.items()):
                if not isinstance(val, str):
                    continue
                new = val
                if (rec_id, field) in REWRITE_FIELD:
                    new = REWRITE_FIELD[(rec_id, field)]
                    rew_hits += 1
                elif rec_id in REWRITE and field == "strDesc":
                    new = REWRITE[rec_id]
                    rew_hits += 1
                for bad, good in TOKEN_MAP.items():
                    if bad in new:
                        tok_hits += new.count(bad)
                        new = new.replace(bad, good)
                if new != val:
                    rec[field] = new
                    touched += 1
        if touched:
            print("  %-30s строк: %d" % (name, touched))
            if write:
                io.open(path, "w", encoding="utf-8", newline=LF).write(
                    json.dumps(data, ensure_ascii=False, indent=2) + LF)
    print(("ПРИМЕНЕНО" if write else "предпросмотр")
          + ": замен токенов %d, переписано строк %d" % (tok_hits, rew_hits))


if __name__ == "__main__":
    main()
