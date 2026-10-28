import math
from PIL import Image, ImageDraw, ImageFilter

def create_tiny11_icon(output_path):
    size = 512
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 1. Background rounded squircle (Fluent dark gradient)
    padding = 24
    corner_radius = 110
    
    # Draw soft outer glow / drop shadow
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow)
    s_draw.rounded_rectangle(
        [padding + 8, padding + 16, size - padding - 8, size - padding + 8],
        radius=corner_radius,
        fill=(0, 100, 220, 90)
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    img.paste(shadow, (0, 0), shadow)

    # Base squircle with vertical blue-slate gradient
    squircle = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sq_draw = ImageDraw.Draw(squircle)
    sq_draw.rounded_rectangle(
        [padding, padding, size - padding, size - padding],
        radius=corner_radius,
        fill=(255, 255, 255, 255)
    )

    # Gradient fill into squircle
    for y in range(padding, size - padding):
        t = (y - padding) / (size - 2 * padding)
        # Deep sapphire to obsidian slate
        r = int(14 * (1 - t) + 10 * t)
        g = int(34 * (1 - t) + 20 * t)
        b = int(61 * (1 - t) + 38 * t)
        for x in range(padding, size - padding):
            if squircle.getpixel((x, y))[3] > 0:
                squircle.putpixel((x, y), (r, g, b, 255))

    # Border stroke highlight (subtle fluent glass border)
    border_draw = ImageDraw.Draw(squircle)
    border_draw.rounded_rectangle(
        [padding, padding, size - padding, size - padding],
        radius=corner_radius,
        outline=(0, 162, 237, 180),
        width=3
    )
    img = Image.alpha_composite(img, squircle)

    # 2. Windows 11 iconic 4-quadrant logo inside
    # Center area: [120, 120] to [392, 392] -> width 272
    q_start = 120
    q_size = 124
    gap = 24
    radius = 18

    # 4 quadrants coordinates
    quadrants = [
        # (x0, y0, x1, y1), color_start, color_end
        ((q_start, q_start, q_start + q_size, q_start + q_size), (0, 120, 215), (0, 180, 255)),            # Top-Left
        ((q_start + q_size + gap, q_start, q_start + 2*q_size + gap, q_start + q_size), (0, 150, 240), (0, 210, 255)), # Top-Right
        ((q_start, q_start + q_size + gap, q_start + q_size, q_start + 2*q_size + gap), (0, 90, 190), (0, 150, 235)),  # Bottom-Left
        ((q_start + q_size + gap, q_start + q_size + gap, q_start + 2*q_size + gap, q_start + 2*q_size + gap), (0, 120, 220), (0, 190, 255)) # Bottom-Right
    ]

    for (x0, y0, x1, y1), c1, c2 in quadrants:
        q_img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        qd = ImageDraw.Draw(q_img)
        qd.rounded_rectangle([x0, y0, x1, y1], radius=radius, fill=(255, 255, 255, 255))
        # Fill each tile with fluent vertical gradient
        for y in range(y0, y1 + 1):
            t = (y - y0) / (y1 - y0)
            r = int(c1[0]*(1-t) + c2[0]*t)
            g = int(c1[1]*(1-t) + c2[1]*t)
            b = int(c1[2]*(1-t) + c2[2]*t)
            for x in range(x0, x1 + 1):
                if q_img.getpixel((x, y))[3] > 0:
                    q_img.putpixel((x, y), (r, g, b, 255))
        
        # Inner subtle highlight on top edge
        qd = ImageDraw.Draw(q_img)
        qd.line([(x0 + radius, y0 + 1), (x1 - radius, y0 + 1)], fill=(255, 255, 255, 140), width=2)
        img = Image.alpha_composite(img, q_img)

    # 3. Add stylish feather / lightning or "TINY" speed badge
    # Stylized feather / wing cut in the bottom right corner of the logo
    badge = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bd = ImageDraw.Draw(badge)
    
    # Neon cyan speed accent / feather curve
    points = [
        (310, 395),
        (355, 345),
        (405, 315),
        (440, 280),
        (415, 340),
        (370, 395),
        (310, 420)
    ]
    bd.polygon(points, fill=(0, 245, 255, 220))
    badge = badge.filter(ImageFilter.GaussianBlur(1))
    img = Image.alpha_composite(img, badge)

    # Subtle text "11" in modern sleek style or minimal emblem
    # Generate multi-size ICO
    icon_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    img.save(output_path, format="ICO", sizes=icon_sizes)
    print(f"Icone creee avec succes : {output_path}")

create_tiny11_icon(r"C:\Users\madtr\Desktop\Tiny11Builder fr\src\Tiny11Builder.App\app.ico")
