"""One-off generator for demo-seed.json — run: python generate_demo_seed.py"""
import json
from pathlib import Path

# E.164 digits without +: 53 + mobile block — unique per demo user
def phone_digits_for_user(ui: int) -> str:
    return str(53500000000 + (ui + 1))

LOCATIONS = [
    ("La Habana — Habana Vieja", 23.1367, -82.3586),
    ("La Habana — Vedado", 23.1247, -82.3882),
    ("Santiago de Cuba", 20.0247, -75.8219),
    ("Camagüey", 21.3789, -77.9153),
    ("Holguín", 20.1357, -76.2634),
    ("Varadero", 23.1394, -81.2861),
    ("Trinidad", 21.8043, -79.9836),
    ("Cienfuegos", 22.1498, -80.4467),
    ("Pinar del Río", 22.4122, -83.6714),
    ("Matanzas", 23.0325, -81.5726),
]

USERS = [
    ("María Isabel Rodríguez", 72),
    ("Carlos Alberto Méndez", 68),
    ("Yunior García Fernández", 75),
    ("Lisandra Torres Acosta", 81),
    ("Reinier López Valdés", 64),
    ("Daniela Pérez Soto", 77),
    ("Alejandro Nuñez Rojas", 70),
    ("Camila Herrera Batista", 83),
    ("Orlando Castillo Vega", 66),
    ("Beatriz Milán Cruz", 79),
]

# Stores per user index 0..9 (1–3 each, total 21)
STORE_COUNTS = [3, 2, 3, 1, 2, 3, 1, 2, 3, 1]

STORE_NAMES = [
    # user 1
    ["Bodega El Morro", "Artesanía Trinidad — taller", "Repuestos bici La Habana"],
    # user 2
    ["Miel y conservas Camagüey", "Servicios eléctricos Yunior"],
    # user 3
    ["Ferretería Santiago Sur", "Textiles hogar Holguín", "Logística ligera Varadero"],
    # user 4
    ["Café y especias Pinar"],
    # user 5
    ["Clínica de bicicletas Matanzas", "Asesoría contable B2B"],
    # user 6
    ["Moda y calzado Cienfuegos", "Insumos agrícolas Daniela", "Transporte local Trinidad"],
    # user 7
    ["Fotografía y video La Habana"],
    # user 8
    ["Fontanería 24h Vedado", "Pintura y drywall Habana"],
    # user 9
    ["Electrodomésticos revisados Santiago", "Mercancía general feria", "Cursos de guitarra online"],
    # user 10
    ["Alimentos frescos Holguín"],
]

PRODUCT_POOL = [
    ("Alimentos", "Miel orgánica frasco 500 g", "MIEL-500", "Miel de flores menor, cristal 500 g.", "Endulzado natural, ideal desayunos.", "Origen Camagüey.", "Nuevo", "12", "Entrega en 48 h."),
    ("Alimentos", "Café molido mezcla 250 g", "CAF-250", "Café tostado molido para greca.", "Aroma fuerte, cuerpo medio.", "Mezcla oriente/sur.", "Nuevo", "8", "Stock limitado."),
    ("Mercancías", "Juego sábanas algodón matrimonial", "TXT-MAT", "Sábanas + fundas, algodón peinado.", "Transpirable, colores tierra.", "280 cm / 240 cm.", "Nuevo", "45", "Retiro en tienda o envío."),
    ("Insumos", "Fertilizante orgánico 5 kg", "FERT-5K", "Compost granulado para huerto urbano.", "Mejora estructura del suelo.", "Uso según etiqueta.", "Nuevo", "22", "Bolsa sellada."),
    ("Mercancías", "Linterna LED recargable USB", "LED-USB", "Linterna aluminio, 800 lm.", "Batería Li-ion incluida.", "IP54 salpicaduras.", "Nuevo", "18", "Garantía 6 meses."),
    ("Alimentos", "Aceite vegetal 900 ml", "ACE-900", "Aceite refinado botella PET.", "Cocina diaria.", "900 ml.", "Nuevo", "6", "Cadena fría recomendada."),
    ("B2B", "Lote guantes nitrilo caja x100", "NIT-X100", "Guantes desechables talla M.", "Taller, clínica, cocina.", "ASTM verificado.", "Nuevo", "35", "Venta por caja."),
    ("Cosechas", "Plántulas de tomate x12", "TOM-X12", "Bandeja 12 unidades.", "Variedad determinada.", "Sustrato enraizado.", "Nuevo", "15", "Retiro en finca."),
]

SERVICE_POOL = [
    ("Servicios", "Instalación de ventilador de techo", "Incluye pernos anclaje y prueba.", "No incluye ventilador.", "Acta de instalación.", "N/A"),
    ("Servicios", "Mantenimiento de bicicleta urbana", "Limpieza cadena, centrado rueda.", "Repuestos mayores.", "Checklist mecánico.", "N/A"),
    ("Asesoría", "Asesoría fiscal PYME (1 h)", "Videollamada + resumen escrito.", "Representación legal.", "Informe PDF.", "Confidencialidad mutua."),
    ("Logística", "Mudanza intra-municipal furgón", "Carga y descarga 2 operarios.", "Embalaje especial.", "Hoja de entrega firmada.", "N/A"),
    ("Servicios", "Clase particular de guitarra (1 h)", "Acústica o eléctrica principiante.", "Instrumento del alumno.", "Plan de práctica semanal.", "N/A"),
    ("Transportista", "Traslado aeropuerto — centro Habana", "Van climatizada, hasta 3 pasajeros.", "Peajes.", "Recibo de servicio.", "N/A"),
]

def product_obj(pid, cat, name, model, short_d, main_b, tech, cond, price, avail):
    return {
        "id": pid,
        "category": cat,
        "name": name,
        "model": model,
        "shortDescription": short_d,
        "mainBenefit": main_b,
        "technicalSpecs": tech,
        "condition": cond,
        "price": price,
        "monedaPrecio": "USD",
        "monedasJson": '["USD","EUR","CUP"]',
        "availability": avail,
        "warrantyReturn": "Según política de la tienda; conservar comprobante.",
        "contentIncluded": "Unidad según descripción.",
        "usageConditions": "Uso conforme a normativa local y manual del fabricante.",
        "published": True,
        "photoUrlsJson": "[]",
        "customFieldsJson": "[]",
    }

def service_obj(sid, cat, tipo, desc, inc, ninc, ent, prop):
    return {
        "id": sid,
        "category": cat,
        "tipoServicio": tipo,
        "descripcion": desc,
        "incluye": inc,
        "noIncluye": ninc,
        "entregables": ent,
        "propIntelectual": prop,
        "monedasJson": '["USD","CUP"]',
        "published": True,
        "customFieldsJson": "[]",
        "photoUrlsJson": "[]",
    }

def main():
    users_out = []
    store_idx = 0
    product_seq = 0
    service_seq = 0

    for ui, ((name, trust), scount) in enumerate(zip(USERS, STORE_COUNTS)):
        uid = f"cuba_demo_u{ui+1:02}"
        phone = phone_digits_for_user(ui)
        pd = f"+53 {phone[2]} {phone[3:6]} {phone[6:8]} {phone[8:11]}"
        stores = []
        for si in range(scount):
            sname = STORE_NAMES[ui][si]
            loc = LOCATIONS[store_idx % len(LOCATIONS)]
            store_idx += 1
            sid = f"{uid}_s{si+1}"
            pitch = (
                f"{sname}: catálogo curado para clientes en {loc[0]}. "
                "Consultas por chat; entregas coordinadas según disponibilidad."
            )
            store = {
                "id": sid,
                "name": sname,
                "verified": (ui + si) % 4 != 0,
                "transportIncluded": (ui + si) % 3 != 0,
                "trustScore": min(95, 70 + (ui * 3 + si * 5) % 25),
                "categories": [
                    ["Alimentos", "Mercancías"],
                    ["Servicios", "Logística"],
                    ["Mercancías", "B2B"],
                    ["Alimentos", "Servicios", "Asesoría"],
                    ["Cosechas", "Insumos"],
                ][(ui + si) % 5],
                "pitch": pitch,
                "joinedAtMs": 1708000000000 + ui * 100000000 + si * 10000000,
                "location": {"lat": loc[1], "lng": loc[2]},
                "websiteUrl": None,
                "products": [],
                "services": [],
            }
            # 3–5 products
            np = 3 + (ui + si) % 3
            for pi in range(np):
                row = PRODUCT_POOL[(product_seq + pi) % len(PRODUCT_POOL)]
                product_seq += 1
                pid = f"cuba_p_u{ui+1:02}_s{si+1}_{pi+1}"
                store["products"].append(
                    product_obj(
                        pid,
                        row[0],
                        row[1],
                        row[2],
                        row[3],
                        row[4],
                        row[5],
                        row[6],
                        row[7],
                        row[8],
                    )
                )
            ns = 3 + (ui + si + 1) % 3
            for vi in range(ns):
                row = SERVICE_POOL[(service_seq + vi) % len(SERVICE_POOL)]
                service_seq += 1
                sid_svc = f"cuba_sv_u{ui+1:02}_s{si+1}_{vi+1}"
                store["services"].append(
                    service_obj(
                        sid_svc,
                        row[0],
                        f"{row[1]} — {sname.split()[0]}",
                        row[1] + " " + loc[0] + ".",
                        row[2],
                        row[3],
                        row[4],
                        row[5],
                    )
                )
            stores.append(store)

        users_out.append(
            {
                "id": uid,
                "phoneDigits": phone,
                "displayName": name,
                "phoneDisplay": pd,
                "avatarUrl": None,
                "trustScore": trust,
                "stores": stores,
            }
        )

    doc = {
        "idempotencyKey": "cuba-demo-2026-04-v1",
        "users": users_out,
    }
    out = Path(__file__).resolve().parent / "demo-seed.json"
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote {out} ({len(users_out)} users)")


if __name__ == "__main__":
    main()
